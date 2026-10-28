using System.Runtime.InteropServices;
using Godot;
using Tank.Client.Cameras;
using Tank.Client.Driving;
using Tank.Client.World;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// Camera, aim, the mouse and the pause layer (blueprint R14), as in the sandbox: the orbit camera follows the own tank
/// and the gun is laid where it looks, unless a fixed preset frames the shot or the player holds free look; a wreck's gun
/// stays put. Dead, the camera walks the spectate chain (<see cref="SpectateState"/>): the wreck close-up, a followed
/// tank, or the free focus WASD drives. The mouse is captured on entering (not in scripted or captured runs), let go by
/// Esc — which brings up the pause layer — or by losing focus, and taken again by 继续 or a click. M opens and closes
/// the full map, which Esc closes first; it neither takes the mouse nor pauses.
/// </summary>
public partial class MatchMain
{
    private const float SniperEyeAboveTrunnion = 0.45f;

    public override void _Input(InputEvent e)
    {
        if (_automation.IgnoresInput) return;
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        // On the results screen the mouse is free, but the host's Enter still has to arrive.
        if (e.IsActionReleased(DriveInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured || (_resultsShown && e is InputEventKey)) _input.OnInputEvent(e);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_automation.IgnoresInput) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when captured && _camera is not null:
                _camera.Look(motion.ScreenRelative);
                break;
            case InputEventMouseButton { Pressed: true } wheel when captured && _camera is not null && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { Pressed: true } when !captured && !_resultsShown && !Paused:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;
        }
        // The scope is the gun's: not for the dead (they hold Shift to speed the free camera), nor paused, on the results or before the own tank is there.
        if (e.IsActionPressed(DriveInput.SniperToggle) && _camera is not null && _client.HasTank && !_dead && !Paused && !_resultsShown) _camera.ToggleSniper();
        // M opens and closes the full map (not over the pause layer or the results, nor while loading hides the map); it
        // neither pauses nor takes the mouse.
        if (e.IsActionPressed(DriveInput.MapToggle) && _minimap is { Visible: true } && !Paused && !_resultsShown)
        {
            _minimap.ToggleFull();
            GetViewport().SetInputAsHandled();
            return;
        }
        // Esc closes an open full map first, and that is all it does.
        if (e.IsActionPressed(DriveInput.UiCancel) && _minimap is { FullOpen: true })
        {
            _minimap.CloseFull();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e.IsActionPressed(DriveInput.UiCancel) && !_resultsShown)
        {
            if (Paused) Resume();
            else ShowPause();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Esc: the mouse goes free and the pause layer comes up (built once, host button and all).</summary>
    private void ShowPause()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
        _minimap?.CloseFull();
        if (_pause is null)
        {
            _pause = new PauseOverlay(_client.IsHost);
            _pause.ResumeRequested += Resume;
            _pause.EndMatchRequested += _client.ReturnToRoom;
            _pause.LeaveRequested += OnLeaveRequested;
            AddChild(_pause);
        }
        _pause.Open();
    }

    /// <summary>继续 (or Esc again): the layer goes and the mouse is captured back, unless the results freed it meanwhile.</summary>
    private void Resume()
    {
        _pause?.Close();
        if (!_resultsShown) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>离开房间: this scene stops touching the client at once; the owner tears the session down (and frees the scene).</summary>
    private void OnLeaveRequested()
    {
        _finished = true;
        LeaveRequested?.Invoke();
    }

    /// <summary>
    /// The camera and the world's focus for this frame (R12: the grass flattens under exactly one tank). At the controls
    /// it is the own tank; dead, the spectate chain picks the wreck, a followed tank, or nothing at all under the free
    /// camera (the world then streams around the camera's eye). A fixed preset keeps framing the own tank, dead or not.
    /// </summary>
    private (TankState?, TankPose?) UpdateView(bool hasTank, in TankState own, TankPose pose, Battlefield field, float fdt)
    {
        if (!hasTank) return (null, null);
        if (!_dead || _automation.HasPreset)
        {
            UpdateCamera(own, pose, field);
            return (own, pose);
        }
        return UpdateSpectate(own, pose, field, fdt);
    }

    /// <summary>Follows the own tank (or looks from a fixed preset), then lays the gun where the camera looks unless a script, free look or a wreck says not to.</summary>
    private void UpdateCamera(in TankState own, TankPose pose, Battlefield field)
    {
        if (_automation.CameraAngles(own.Yaw) is { } angles) _camera.SetAngles(angles.Yaw, angles.Pitch);
        Vector3 pivot = pose.Position.ToGodot();
        _camera.Follow(pivot, (pose.Trunnion(own.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), field.Terrain, field.WaterLevel);
        if (_automation.HasPreset)
        {
            string preset = _automation.Preset!;
            var (eye, target) = CameraPresets.Get(preset, pivot, own.Yaw, _time, _groundAt, targetIndex: 0, site: _site);
            _mapViewOn = CameraPresets.Look(_camera.Camera, preset, eye, target, _site, _world!, _mapViewOn);
        }
        else if (!own.IsDestroyed && !_input.FreeLook)
        {
            (_aimYaw, _aimPitch) = AimSolver.Solve(_camera, field, pose.Trunnion(own.Spec), _aimYaw);
        }
    }

    /// <summary>
    /// One spectating frame (blueprint §5 T8): Q, E and a click step through the living candidates, F frees the camera
    /// (free-for-all only), and <see cref="SpectateState"/> runs the close-up and the fallbacks. Returns what the world
    /// should treat as the followed tank.
    /// </summary>
    private (TankState?, TankPose?) UpdateSpectate(in TankState own, TankPose ownPose, Battlefield field, float fdt)
    {
        FillCandidates();
        ReadOnlySpan<int> candidates = CollectionsMarshal.AsSpan(_candidates);
        int step = _input.TakeSpectateStep() + (_input.TakeSpectateClick() ? 1 : 0);
        if (step != 0) _spectate.Step(step, candidates);
        bool freeForAll = Mode == BattleMode.FreeForAll;
        if (_input.TakeSpectateFree() && freeForAll) _spectate.GoFree();
        _spectate.Update(fdt, candidates, freeWhenNone: freeForAll);

        (_spectateName, _spectateFree) = (null, false);
        if (_spectate.Current == SpectateState.Mode.Follow && TryFindRemote(_spectate.TargetId, out RemoteTank remote))
        {
            TankState target = RenderMath.FromRemote(remote);
            TankPose pose = TankPose.Compute(target, field.Terrain);
            _focus = target.Position;
            _spectateName = SpectateNameOf(target.Id);
            _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(target.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), field.Terrain, field.WaterLevel);
            return (target, pose);
        }
        if (_spectate.Current == SpectateState.Mode.Free)
        {
            _spectateFree = true;
            var (throttle, steer, _) = Paused ? default : _input.Peek();
            _focus = SpectateState.MoveFocus(_focus, _camera.Yaw, throttle, steer, _input.SpectateFast, fdt, field);
            // The focus rides on the water where there is some (stage 5 design §2), so the camera never sinks into it.
            float surface = field.Terrain.HeightAt(_focus.X, _focus.Y);
            if (field.WaterLevel is { } water) surface = MathF.Max(surface, water);
            Vector3 point = _focus.Ground(surface);
            _camera.Follow(point, point, field.Terrain, field.WaterLevel);
            return (null, null);
        }
        UpdateCamera(own, ownPose, field);   // the wreck close-up (and the odd frame a followed tank just vanished)
        return (own, ownPose);
    }

    /// <summary>The tanks a dead player may watch, in the interpolator's order: living teammates in a team mode, everyone alive in a free-for-all.</summary>
    private void FillCandidates()
    {
        _candidates.Clear();
        bool teams = ModeRules.HasTeams(Mode);
        byte ownTeam = OwnTeam(_client.Own);
        IReadOnlyList<RemoteTank> others = _client.Others;
        for (int i = 0; i < others.Count; i++)
        {
            BattleEntity entity = others[i].Entity;
            if (entity.Has(TankFlags.Destroyed) || (teams && entity.Team != ownTeam)) continue;
            _candidates.Add(entity.Id);
        }
    }

    private bool TryFindRemote(int tankId, out RemoteTank remote)
    {
        IReadOnlyList<RemoteTank> others = _client.Others;
        for (int i = 0; i < others.Count; i++)
        {
            if (others[i].Entity.Id != tankId) continue;
            remote = others[i];
            return true;
        }
        remote = default;
        return false;
    }

    /// <summary>The name on the spectate bar, from the scoreboard's tank ids by room slot.</summary>
    private string SpectateNameOf(int tankId)
    {
        int slot = _client.Latest is { } latest ? MatchHudFrame.SlotOf(latest.Players, tankId) : -1;
        return slot >= 0 && _names[slot] is { } name ? name : UnknownName;
    }
}
