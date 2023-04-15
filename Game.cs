using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Hypostasis.Game.Structures;

namespace Cammy;

[HypostasisInjection]
public static unsafe class Game
{
    public static bool EnableSpectating { get; set; } = false;

    private static float GetZoomDeltaDetour() => PresetManager.CurrentPreset.ZoomDelta;

    // Of course this isn't though
    [HypostasisSignatureInjection("F3 0F 59 05 ?? ?? ?? ?? 0F 28 74 24 20 48 83 C4 30 5B C3 0F 57 C0 0F", Static = true, Required = true)] // F3 0F 59 05 ?? ?? ?? ?? 0F 28 74 24 20 48 83 C4 30 5B C3 0F 57 C0 0F 28 74 24 20 48 83 C4 30 5B C3
    private static float* foVDeltaPtr;
    public static float FoVDelta // 0.08726646751
    {
        get => foVDeltaPtr != null ? *foVDeltaPtr : 0;
        set
        {
            if (foVDeltaPtr != null)
                *foVDeltaPtr = value;
        }
    }

    private static void SetCameraLookAtDetour(GameCamera* camera, Vector3* lookAtPosition, Vector3* cameraPosition, Vector3* a4) // a4 seems to be immediately overwritten and unused
    {
        if (FreeCam.Enabled) return;
        camera->VTable.setCameraLookAt.Original(camera, lookAtPosition, cameraPosition, a4);
    }

    private static void GetCameraPositionDetour(GameCamera* camera, GameObject* target, Vector3* position, Bool swapPerson)
    {
        if (!FreeCam.Enabled)
        {
            var preset = PresetManager.CurrentPreset;

            var cameraTarget = GetCameraTargetDetour(camera);
            if (preset.EnableViewBobbing && Common.getLocalBonePosition.IsValid && cameraTarget != null && cameraTarget->DrawObject != null && Common.GetLocalBonePosition(cameraTarget, 26) is var offset && offset != Vector3.Zero)
                *position = offset + (Vector3)cameraTarget->DrawObject->Object.Position;
            else
                camera->VTable.getCameraPosition.Original(camera, target, position, swapPerson);

            position->Y += preset.HeightOffset;

            if (preset.SideOffset == 0 || camera->mode != 1) return;

            const float halfPI = MathF.PI / 2f;
            var a = Common.CameraManager->worldCamera->currentHRotation - halfPI;
            position->X += -preset.SideOffset * MathF.Sin(a);
            position->Z += -preset.SideOffset * MathF.Cos(a);
        }
        else
        {
            *position = FreeCam.Position;
        }
    }

    public static bool IsSpectating { get; private set; } = false;
    private static GameObject* GetCameraTargetDetour(GameCamera* camera)
    {
        if (EnableSpectating)
        {
            if (DalamudApi.TargetManager.FocusTarget is { } focus)
            {
                IsSpectating = true;
                return (GameObject*)focus.Address;
            }

            if (DalamudApi.TargetManager.SoftTarget is { } soft)
            {
                IsSpectating = true;
                return (GameObject*)soft.Address;
            }
        }

        if (Cammy.Config.DeathCamMode == 1 && DalamudApi.Condition[ConditionFlag.Unconscious] && DalamudApi.TargetManager.Target is { } target)
        {
            IsSpectating = true;
            return (GameObject*)target.Address;
        }

        IsSpectating = false;
        return camera->VTable.getCameraTarget.Original(camera);
    }

    private static Bool CanChangePerspectiveDetour() => !FreeCam.Enabled;

    private static byte GetCameraAutoRotateModeDetour(GameCamera* camera, Framework* framework) => (byte)(FreeCam.Enabled || IsSpectating ? 4 : GameCamera.getCameraAutoRotateMode.Original(camera, framework));

    private static float GetCameraMaxMaintainDistanceDetour(GameCamera* camera) => GameCamera.getCameraMaxMaintainDistance.Original(camera) is var ret && ret < 10f ? ret : camera->maxZoom;

    [HypostasisSignatureInjection("F3 0F 10 05 ?? ?? ?? ?? 0F 2E C6 0F 8A", Offset = 4, Static = true, Required = true)] // Also found at g_PlayerMoveController + 0x54C
    private static nint forceDisableMovementPtr;
    public static ref int ForceDisableMovement => ref *(int*)forceDisableMovementPtr; // Increments / decrements by 1 to allow multiple things to disable movement at the same time

    public static readonly AsmPatch cameraNoClippyReplacer = new("E8 ?? ?? ?? ?? 45 0F 57 FF", new byte[] { 0x30, 0xC0, 0x90, 0x90, 0x90 }, Cammy.Config.EnableCameraNoClippy); // E8 ?? ?? ?? ?? 48 8B B4 24 E0 00 00 00 40 32 FF (0x90, 0x90, 0x90, 0x90, 0x90)

    private static AsmPatch addMidHookReplacer;

    public static float GetDefaultLookAtHeightOffset()
    {
        var worldCamera = Common.CameraManager->worldCamera;
        if (worldCamera == null || DalamudApi.ClientState.LocalPlayer is not { } p) return 0;

        var prev = worldCamera->lookAtHeightOffset;
        GameCamera.updateLookAtHeightOffset.Original(worldCamera, (GameObject*)p.Address, false);
        var ret = worldCamera->lookAtHeightOffset;
        worldCamera->lookAtHeightOffset = prev;
        return ret;
    }

    public static Bool UpdateLookAtHeightOffsetDetour(GameCamera* camera, GameObject* o, Bool zero)
    {
        var ret = GameCamera.updateLookAtHeightOffset.Original(camera, o, zero);
        if (ret && !zero && (nint)o == DalamudApi.ClientState.LocalPlayer?.Address)
            camera->lookAtHeightOffset = PresetManager.CurrentPreset.LookAtHeightOffset;
        return ret;
    }

    public static void Initialize()
    {
        if (Common.CameraManager == null || !Common.IsValid(Common.CameraManager->worldCamera) || !Common.IsValid(Common.InputData))
            throw new ApplicationException("Failed to validate core structures!");

        var vtbl = Common.CameraManager->worldCamera->VTable;
        vtbl.setCameraLookAt.CreateHook(SetCameraLookAtDetour);
        vtbl.getCameraPosition.CreateHook(GetCameraPositionDetour);
        vtbl.getCameraTarget.CreateHook(GetCameraTargetDetour);
        vtbl.canChangePerspective.CreateHook(CanChangePerspectiveDetour);
        vtbl.getZoomDelta.CreateHook(GetZoomDeltaDetour);

        GameCamera.getCameraAutoRotateMode.CreateHook(GetCameraAutoRotateModeDetour); // Found inside Client__Game__Camera_UpdateRotation
        GameCamera.getCameraMaxMaintainDistance.CreateHook(GetCameraMaxMaintainDistanceDetour); // Found 1 function deep inside Client__Game__Camera_vf3
        GameCamera.updateLookAtHeightOffset.CreateHook(UpdateLookAtHeightOffsetDetour);

        // Gross workaround for fixing legacy control's maintain distance
        var address = DalamudApi.SigScanner.ScanModule("48 85 C9 74 24 48 83 C1 10");
        var offset = BitConverter.GetBytes(GameCamera.getCameraMaxMaintainDistance.Address - (address + 0x8));

        // mov rcx, rbx
        // call offset
        // jmp 27h
        addMidHookReplacer = new(address,
            new byte[] {
                    0x48, 0x8B, 0xCB,
                    0xE8, offset[0], offset[1], offset[2], offset[3],
                    0xEB, 0x27,
                    0x90, 0x90, 0x90, 0x90
            },
            true);
    }

    public static void Dispose() { }
}