using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroSynth.Flow;

namespace ZeroSynth.UI
{
    /// <summary>Shares one settings binding between menu and pause without owning game state.</summary>
    public sealed class SettingsPanelController : IDisposable
    {
        readonly VisualElement m_Root;
        readonly Slider m_Master;
        readonly Slider m_Music;
        readonly Slider m_Sfx;
        readonly DropdownField m_Quality;
        readonly Toggle m_DebugHud;
        readonly Button m_Back;

        public event Action Closed;

        public SettingsPanelController(VisualElement root)
        {
            m_Root = root;
            m_Master = root.Q<Slider>("master-slider");
            m_Music = root.Q<Slider>("music-slider");
            m_Sfx = root.Q<Slider>("sfx-slider");
            m_Quality = root.Q<DropdownField>("quality-dropdown");
            m_DebugHud = root.Q<Toggle>("debug-hud-toggle");
            m_Back = root.Q<Button>("settings-back-button");
            m_Quality.choices = new List<string>(QualitySettings.names);
            Refresh();
            m_Master.RegisterValueChangedCallback(OnVolumeChanged);
            m_Music.RegisterValueChangedCallback(OnVolumeChanged);
            m_Sfx.RegisterValueChangedCallback(OnVolumeChanged);
            m_Quality.RegisterValueChangedCallback(OnQualityChanged);
            m_DebugHud.RegisterValueChangedCallback(OnDebugChanged);
            m_Back.clicked += Close;
        }

        public void Show()
        {
            Refresh();
            m_Root.style.display = DisplayStyle.Flex;
            m_Back.Focus();
        }

        public void Hide() => m_Root.style.display = DisplayStyle.None;

        void Refresh()
        {
            var flow = GameFlow.Instance;
            bool available = flow != null && flow.Settings != null;
            m_Master.SetEnabled(available);
            m_Music.SetEnabled(available);
            m_Sfx.SetEnabled(available);
            m_Quality.SetEnabled(available && m_Quality.choices.Count > 0);
            m_DebugHud.SetEnabled(available);
            if (!available) return;

            var settings = flow.Settings;
            m_Master.SetValueWithoutNotify(settings.MasterVolume);
            m_Music.SetValueWithoutNotify(settings.MusicVolume);
            m_Sfx.SetValueWithoutNotify(settings.SfxVolume);
            int quality = settings.QualityLevel < 0 ? QualitySettings.GetQualityLevel() : settings.QualityLevel;
            if (m_Quality.choices.Count > 0)
                m_Quality.SetValueWithoutNotify(m_Quality.choices[Mathf.Clamp(quality, 0, m_Quality.choices.Count - 1)]);
            m_DebugHud.SetValueWithoutNotify(settings.ShowDebugHud);
        }

        void OnVolumeChanged(ChangeEvent<float> evt) => Save();
        void OnQualityChanged(ChangeEvent<string> evt) => Save();
        void OnDebugChanged(ChangeEvent<bool> evt) => Save();

        void Save()
        {
            var flow = GameFlow.Instance;
            if (flow == null || flow.Settings == null) return;
            var settings = flow.Settings;
            settings.MasterVolume = m_Master.value;
            settings.MusicVolume = m_Music.value;
            settings.SfxVolume = m_Sfx.value;
            settings.QualityLevel = m_Quality.index;
            settings.ShowDebugHud = m_DebugHud.value;
            flow.CommitSettings();
        }

        void Close()
        {
            Hide();
            Closed?.Invoke();
        }

        public void Dispose()
        {
            m_Master.UnregisterValueChangedCallback(OnVolumeChanged);
            m_Music.UnregisterValueChangedCallback(OnVolumeChanged);
            m_Sfx.UnregisterValueChangedCallback(OnVolumeChanged);
            m_Quality.UnregisterValueChangedCallback(OnQualityChanged);
            m_DebugHud.UnregisterValueChangedCallback(OnDebugChanged);
            m_Back.clicked -= Close;
        }
    }
}
