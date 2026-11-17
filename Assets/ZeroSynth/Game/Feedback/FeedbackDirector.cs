using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZeroSynth.Audio;
using ZeroSynth.Build;
using ZeroSynth.Flow;
using Random = UnityEngine.Random;

namespace ZeroSynth.Feedback
{
    /// <summary>
    /// Turns simulation events into tracers, death bursts and sound.
    /// </summary>
    /// <remarks>
    /// Everything here is capped per frame and drops the overflow. A wave can
    /// kill hundreds of agents in one frame; drawing and sounding every one of
    /// them would cost more than the simulation, and past a few dozen nobody can
    /// tell the difference. The queues are cleared after each drain, so a
    /// backlog never builds up and plays back late.
    ///
    /// One ParticleSystem emits every burst, rather than a system per death.
    /// </remarks>
    public sealed class FeedbackDirector : MonoBehaviour
    {
        [SerializeField] Material m_TracerMaterial;
        [SerializeField] Material m_ParticleMaterial;

        [Header("Limits")]
        [SerializeField, Min(1)] int m_MaxTracers = 48;
        [SerializeField, Min(1)] int m_MaxShotsPerFrame = 40;
        [SerializeField, Min(1)] int m_MaxDeathsPerFrame = 60;

        static readonly Color s_TurretColor = new Color(0.4f, 0.9f, 1f, 1f);
        static readonly Color s_AcidColor = new Color(0.6f, 0.9f, 0.2f, 1f);
        static readonly Color s_FleshColor = new Color(0.45f, 0.6f, 0.2f, 1f);
        static readonly Color s_BloodColor = new Color(0.75f, 0.1f, 0.1f, 1f);

        World m_World;
        EntityManager m_EntityManager;
        EntityQuery m_Query;

        LineRenderer[] m_Tracers;
        float[] m_Life;
        float[] m_MaxLife;
        Color[] m_BaseColor;
        int m_NextTracer;

        ParticleSystem m_Burst;
        BuildController m_Build;
        int m_LastWave;

        void Awake()
        {
            m_Tracers = new LineRenderer[m_MaxTracers];
            m_Life = new float[m_MaxTracers];
            m_MaxLife = new float[m_MaxTracers];
            m_BaseColor = new Color[m_MaxTracers];

            for (int i = 0; i < m_MaxTracers; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);

                var line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = 0.14f;
                line.endWidth = 0.04f;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = m_TracerMaterial;
                line.enabled = false;
                m_Tracers[i] = line;
            }

            BuildBurstSystem();
        }

        void BuildBurstSystem()
        {
            var go = new GameObject("DeathBurst");
            go.transform.SetParent(transform, false);

            m_Burst = go.AddComponent<ParticleSystem>();
            m_Burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = m_Burst.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 3000;
            main.startSpeed = 0f;
            main.startLifetime = 0.55f;
            main.startSize = 0.28f;
            main.gravityModifier = 1.3f;

            var emission = m_Burst.emission;
            emission.enabled = false;

            var shape = m_Burst.shape;
            shape.enabled = false;

            var fade = m_Burst.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var shrink = m_Burst.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = m_ParticleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            m_Burst.Play();
        }

        void OnDestroy()
        {
            if (m_Build != null) m_Build.Message -= OnBuildMessage;
        }

        void Update()
        {
            FadeTracers();
            WatchBuild();
            WatchWave();

            if (!Bind()) return;

            var feedback = m_EntityManager.GetComponentObject<SimFeedback>(m_Query.GetSingletonEntity());
            DrainShots(feedback);
            DrainDeaths(feedback);
            ReadCounters(feedback);
        }

        bool Bind()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                m_World = null;
                return false;
            }

            // The world is replaced between runs, so queries must be rebuilt.
            if (world != m_World)
            {
                m_EntityManager = world.EntityManager;
                m_Query = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SimFeedback>());
                m_World = world;
            }

            return !m_Query.IsEmpty;
        }

        void DrainShots(SimFeedback feedback)
        {
            int turret = 0, acid = 0, drawn = 0;

            while (drawn < m_MaxShotsPerFrame && feedback.Shots.TryDequeue(out var shot))
            {
                SpawnTracer(shot);
                drawn++;
                if (shot.Kind == ShotKind.Acid) acid++; else turret++;
            }

            // Whatever is left would only play back late.
            feedback.Shots.Clear();

            var audio = AudioDirector.Instance;
            if (audio == null) return;

            if (turret > 0) audio.Play(Sfx.Shot, Mathf.Min(1f, 0.25f + 0.05f * turret), 0.045f);
            if (acid > 0) audio.Play(Sfx.Acid, Mathf.Min(1f, 0.3f + 0.1f * acid), 0.12f);
        }

        void SpawnTracer(ShotEvent shot)
        {
            int i = m_NextTracer;
            m_NextTracer = (m_NextTracer + 1) % m_Tracers.Length;

            var line = m_Tracers[i];
            line.SetPosition(0, (Vector3)shot.From);
            line.SetPosition(1, (Vector3)shot.To);

            bool acid = shot.Kind == ShotKind.Acid;
            m_BaseColor[i] = acid ? s_AcidColor : s_TurretColor;
            m_MaxLife[i] = acid ? 0.3f : 0.09f;
            m_Life[i] = m_MaxLife[i];

            line.startColor = line.endColor = m_BaseColor[i];
            line.enabled = true;
        }

        void FadeTracers()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < m_Tracers.Length; i++)
            {
                if (m_Life[i] <= 0f) continue;

                m_Life[i] -= dt;
                if (m_Life[i] <= 0f)
                {
                    m_Tracers[i].enabled = false;
                    continue;
                }

                var c = m_BaseColor[i];
                c.a = m_Life[i] / m_MaxLife[i];
                m_Tracers[i].startColor = m_Tracers[i].endColor = c;
            }
        }

        void DrainDeaths(SimFeedback feedback)
        {
            int count = 0;
            float largest = 0f;

            while (count < m_MaxDeathsPerFrame && feedback.Deaths.TryDequeue(out var death))
            {
                EmitBurst(death);
                count++;
                largest = Mathf.Max(largest, death.Size);
            }

            feedback.Deaths.Clear();

            if (count == 0) return;

            var audio = AudioDirector.Instance;
            if (audio == null) return;

            // A boss dying should sound like one: bigger, and never throttled away.
            if (largest >= 3f)
                audio.Play(Sfx.Death, 1f, 0f, 0.02f);
            else
                audio.Play(Sfx.Death, Mathf.Min(1f, 0.2f + 0.06f * count), 0.05f);
        }

        void EmitBurst(DeathEvent death)
        {
            int particles = Mathf.Clamp((int)(4f + death.Size * 3f), 4, 24);

            var emit = new ParticleSystem.EmitParams
            {
                position = (Vector3)death.Position + Vector3.up * (death.Size * 0.45f),
                startLifetime = Random.Range(0.4f, 0.7f),
                startSize = 0.16f + death.Size * 0.06f
            };

            for (int i = 0; i < particles; i++)
            {
                emit.velocity = Random.insideUnitSphere * (1.5f + death.Size) + Vector3.up * 2.5f;
                emit.startColor = Color.Lerp(s_FleshColor, s_BloodColor, Random.value < 0.3f ? 1f : Random.value * 0.4f);
                m_Burst.Emit(emit, 1);
            }
        }

        void ReadCounters(SimFeedback feedback)
        {
            var audio = AudioDirector.Instance;

            if (audio != null)
            {
                if (feedback.CoreDamage > 0f) audio.Play(Sfx.Hit, 1f, 0.3f);
                else if (feedback.StructuresLost > 0) audio.Play(Sfx.Hit, 0.8f, 0.2f);
            }

            feedback.Hits = 0;
            feedback.CoreDamage = 0f;
            feedback.StructuresLost = 0;
        }

        void WatchWave()
        {
            var flow = GameFlow.Instance;
            if (flow == null) return;

            var snapshot = flow.Snapshot;
            if (!snapshot.Valid)
            {
                m_LastWave = 0;
                return;
            }

            if (snapshot.Wave != m_LastWave)
            {
                if (snapshot.Wave > 0 && AudioDirector.Instance != null)
                    AudioDirector.Instance.Play(Sfx.WaveStart, 0.9f);
                m_LastWave = snapshot.Wave;
            }
        }

        /// <summary>BuildController appears after this does, so it is picked up lazily.</summary>
        void WatchBuild()
        {
            var build = BuildController.Instance;
            if (ReferenceEquals(build, m_Build)) return;

            if (m_Build != null) m_Build.Message -= OnBuildMessage;
            m_Build = build;
            if (m_Build != null) m_Build.Message += OnBuildMessage;
        }

        void OnBuildMessage(string message)
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;

            audio.Play(message.EndsWith("BUILT") ? Sfx.Build : Sfx.Error, 0.8f, 0.05f, 0.02f);
        }
    }
}
