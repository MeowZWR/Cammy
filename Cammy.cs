using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Cammy;

public class Cammy(IDalamudPluginInterface pluginInterface) : DalamudPlugin<Configuration>(pluginInterface), IDalamudPlugin
{
    protected override void Initialize()
    {
        Game.Initialize();
        IPC.Initialize();
        DalamudApi.ClientState.Login += Login;
    }

    protected override void ToggleConfig() => PluginUI.IsVisible ^= true;

    private const string cammySubcommands = "/cammy [ 帮助 | 预设 | 焦距 | 视场角 | 观看 | 无碰撞 | 自由相机 ]";

    [PluginCommand("/cammy", HelpMessage = "打开/关闭配置。命令语法：" + cammySubcommands)]
    private unsafe void ToggleConfig(string command, string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            ToggleConfig();
            return;
        }

        var regex = Regex.Match(argument, "^(\\w+) ?(.*)");
        var subcommand = regex.Success && regex.Groups.Count > 1 ? regex.Groups[1].Value : string.Empty;

        switch (subcommand.ToLower())
        {
            case "预设":
                {
                    if (regex.Groups.Count < 2 || string.IsNullOrEmpty(regex.Groups[2].Value))
                    {
                        PresetManager.CurrentPreset = null;
                        DalamudApi.PrintEcho("已移除预设覆盖。");
                        return;
                    }

                    var arg = regex.Groups[2].Value;
                    var preset = Config.Presets.FirstOrDefault(preset => preset.Name == arg);

                    if (preset == null)
                    {
                        DalamudApi.PrintError($"未找到预设 \"{arg}\"");
                        return;
                    }

                    PresetManager.CurrentPreset = preset;
                    DalamudApi.PrintEcho($"预设已设置为 \"{arg}\"");
                    break;
                }
            case "焦距":
                {
                    if (regex.Groups.Count < 2 || !float.TryParse(regex.Groups[2].Value, out var amount))
                    {
                        DalamudApi.PrintError("无效数值。");
                        return;
                    }

                    Common.CameraManager->worldCamera->currentZoom = amount;
                    break;
                }
            case "视场角":
                {
                    if (regex.Groups.Count < 2 || !float.TryParse(regex.Groups[2].Value, out var amount))
                    {
                        DalamudApi.PrintError("为无效数值。");
                        return;
                    }

                    Common.CameraManager->worldCamera->currentFoV = amount;
                    break;
                }
            case "观看":
                {
                    Game.EnableSpectating ^= true;
                    DalamudApi.PrintEcho($"观看模式现在{(Game.EnableSpectating ? "已启用" : "已禁用")}!");
                    break;
                }
            case "无碰撞":
                {
                    Config.EnableCameraNoClippy ^= true;
                    if (!FreeCam.Enabled)
                        Game.cameraNoClippyReplacer.Toggle();
                    Config.Save();
                    DalamudApi.PrintEcho($"相机碰撞现在{(Config.EnableCameraNoClippy ? "已禁用" : "已启用")}!");
                    break;
                }
            case "自由相机":
                {
                    FreeCam.Toggle();
                    break;
                }
            case "帮助":
                {
                    DalamudApi.PrintEcho("子命令：" +
                        "\n预设 <预设名称> - 使用指定名称的预设替代当前镜头设置。不带预设名称来还原镜头设置。" +
                        "\n焦距 <变焦缩放数值> - 设置当前缩放级别。" +
                        "\n视场角 <视场角数值> - 设置当前视场角级别。" +
                        "\n观看 - 开关 \"观看焦点 / 软目标\" 选项。" +
                        "\n无碰撞 - 开关 \"禁用相机碰撞\" 选项。" +
                        "\n自由相机 - 开关 \"自由相机\" 选项。");
                    break;
                }
            default:
                {
                    DalamudApi.PrintError("无效的用法：" + cammySubcommands);
                    break;
                }
        }
    }

    protected override void Update()
    {
        FreeCam.Update();
        PresetManager.Update();
    }

    protected override void Draw() => PluginUI.Draw();

    private static void Login()
    {
        DalamudApi.Framework.Update += UpdateDefaultPreset;
        PresetManager.DisableCameraPresets();
        PresetManager.CheckCameraConditionSets(true);
    }

    private static void UpdateDefaultPreset(IFramework framework)
    {
        if (DalamudApi.Condition[ConditionFlag.BetweenAreas]) return;
        PresetManager.DefaultPreset = new();
        DalamudApi.Framework.Update -= UpdateDefaultPreset;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        IPC.Dispose();
        PresetManager.DefaultPreset.Apply();
        DalamudApi.ClientState.Login -= Login;

        if (FreeCam.Enabled)
            FreeCam.Toggle();

        Game.Dispose();
    }
}