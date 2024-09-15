using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dalamud.Configuration;

namespace Cammy;

public class CameraConfigPreset
{
    public enum ViewBobSetting
    {
        禁用,
        [Display(Name = "第一人称时")] FirstPerson,
        [Display(Name = "脱离战斗时")] OutOfCombat,
        任何情况下
    }

    public string Name = "新预设";

    public bool UseStartOnLogin = false;

    public bool UseStartZoom = false;
    public float StartZoom = 6;
    public float MinZoom = 1.5f;
    public float MaxZoom = 20;
    public float ZoomDelta = 0.75f;

    public bool UseStartFoV = false;
    public float StartFoV = 0.78f;
    public float MinFoV = 0.69f;
    public float MaxFoV = 0.78f;
    public float FoVDelta = 0.08726646751f;

    public float MinVRotation = -1.483530f;
    public float MaxVRotation = 0.785398f;

    public float HeightOffset = 0;
    public float SideOffset = 0;
    public float Tilt = 0;
    public float LookAtHeightOffset = Game.GetDefaultLookAtHeightOffset() ?? 0;
    public ViewBobSetting ViewBobMode = ViewBobSetting.禁用;
    public int ConditionSet = -1;

    public CameraConfigPreset Clone() => (CameraConfigPreset)MemberwiseClone();

    public bool CheckConditionSet() => ConditionSet < 0 || IPC.QoLBarEnabled && IPC.CheckConditionSet(ConditionSet);

    public void Apply(bool isLoggingIn = false) => PresetManager.ApplyPreset(this, isLoggingIn);
}

public class Configuration : PluginConfiguration, IPluginConfiguration
{
    public enum DeathCamSetting
    {
        禁用,
        观看,
        [Display(Name = "自由相机")] FreeCam
    }

    public int Version { get; set; }

    public List<CameraConfigPreset> Presets = [];
    public bool EnableCameraNoClippy = false;
    public DeathCamSetting DeathCamMode = DeathCamSetting.禁用;
}