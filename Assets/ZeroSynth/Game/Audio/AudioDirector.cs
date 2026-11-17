using System.Collections.Generic;
using UnityEngine;
using ZeroSynth.Flow;

namespace ZeroSynth.Audio
{
    /// <summary>
    /// Owns every AudioSource and the two volume sliders that scale them.
    /// </summary>
    /// <remarks>
    /// Lives on the GameFlow object, so audio survives scene loads and the
    /// music does not restart between the menu and a run. Master volume is
    /// applied by GameFlow through AudioListener; this scales music and effects
    /// on top of it.
    ///
    /// Effects go through a small pool of sources rather than one, because
    /// pitch is a property of the source: a single source would shift every
    /// sound already playing each time one was played with a different pitch.
    /// </remarks>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        const int PoolSize = 8;
        const float MusicLevel = 0.35f;

        readonly Dictionary<Sfx, AudioClip> m_Clips = new Dictionary<Sfx, AudioClip>();
        readonly Dictionary<Sfx, float> m_NextAllowed = new Dictionary<Sfx, float>();

        AudioSource[] m_Pool;
        AudioSource m_Music;
        int m_Next;
        GameFlow m_Flow;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            m_Pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                m_Pool[i] = source;
            }

            m_Music = gameObject.AddComponent<AudioSource>();
            m_Music.playOnAwake = false;
            m_Music.loop = true;
            m_Music.spatialBlend = 0f;
            m_Music.clip = ProceduralAudio.CreateMusic();

            foreach (Sfx id in System.Enum.GetValues(typeof(Sfx)))
                m_Clips[id] = ProceduralAudio.Create(id);

            m_Music.Play();
            ApplyVolumes();
        }

        void Update()
        {
            var flow = GameFlow.Instance;
            if (ReferenceEquals(flow, m_Flow)) return;

            if (m_Flow != null)
            {
                m_Flow.StateChanged -= OnStateChanged;
                m_Flow.SettingsChanged -= ApplyVolumes;
            }

            m_Flow = flow;
            if (m_Flow != null)
            {
                m_Flow.StateChanged += OnStateChanged;
                m_Flow.SettingsChanged += ApplyVolumes;
                ApplyVolumes();
            }
        }

        void OnDestroy()
        {
            if (m_Flow != null)
            {
                m_Flow.StateChanged -= OnStateChanged;
                m_Flow.SettingsChanged -= ApplyVolumes;
            }

            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Plays a sound. <paramref name="minInterval"/> drops requests that come
        /// too soon after the last of the same sound, so a thousand kills in a
        /// frame is one thump rather than a wall of noise.
        /// </summary>
        public void Play(Sfx id, float volume = 1f, float minInterval = 0f, float pitchJitter = 0.06f)
        {
            if (m_Pool == null || !m_Clips.TryGetValue(id, out var clip)) return;

            float now = Time.unscaledTime;
            if (minInterval > 0f)
            {
                if (m_NextAllowed.TryGetValue(id, out float next) && now < next) return;
                m_NextAllowed[id] = now + minInterval;
            }

            var source = m_Pool[m_Next];
            m_Next = (m_Next + 1) % m_Pool.Length;

            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(clip, Mathf.Clamp01(volume) * SfxVolume());
        }

        void OnStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.Victory) Play(Sfx.Victory, 0.9f);
            else if (next == GameState.GameOver) Play(Sfx.Defeat, 0.9f);
        }

        float SfxVolume()
        {
            return m_Flow != null && m_Flow.Settings != null ? m_Flow.Settings.SfxVolume : 1f;
        }

        void ApplyVolumes()
        {
            float music = m_Flow != null && m_Flow.Settings != null ? m_Flow.Settings.MusicVolume : 0.8f;
            if (m_Music != null) m_Music.volume = music * MusicLevel;
        }
    }
}
