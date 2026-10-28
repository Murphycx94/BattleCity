using System.Diagnostics;
using Godot;
using Tank.Client.Art;
using Tank.Client.Match;
using Tank.Client.Overlay;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Client.Minimap;

/// <summary>
/// The maps of a scene (stage 5 design §5): the corner map at the bottom right (<see cref="HudLayout.Minimap"/>), and the
/// full-screen one (<see cref="HudLayout.FullMap"/>) over a dimmed screen, with the map's name inside its top edge and
/// "M / Esc 关闭" under it; while the full map is open the corner one hides. Both are placed by <see cref="HudLayout.For"/>
/// for the viewport's size and follow it when the window is resized.
/// <para>
/// Owns the picture: <see cref="Build"/> bakes the ground on a worker thread (a fault is logged from the worker, so no
/// bake ends unobserved, even one nobody waits for any more); the first <see cref="Show"/> after it is done paints the
/// houses and walls (<see cref="MinimapImage"/>, on the scene's thread, as they stand) and uploads the texture
/// (<see cref="HasPicture"/>). From then on each <see cref="Show"/> repaints the tiles of the houses and walls that rose
/// or fell, and the texture follows at most every <see cref="UploadIntervalMs"/> (a run of falls costs one upload per
/// interval, and the last of them is uploaded once they settle). One scene, one map: the layer knows its map by the
/// terrain it was built with; a battlefield on the same terrain whose props are not the ones the picture was painted from
/// gets a new picture from the same ground, one on another terrain is an error (logged once; the old picture stays).
/// Layer 2, with the match HUD (the one added later is on top); the pause layer (3) covers it.
/// </para>
/// </summary>
public partial class MinimapLayer : CanvasLayer
{
    private const int MapLayer = 2;
    private const float DimAlpha = 0.6f;
    private const int NameSize = 18, HintSize = 16, CaptionOutline = 4;
    private const float NameInset = 14f;   // from the full map's top edge to the middle of the name
    private const string Hint = "M / Esc 关闭";
    private const ulong UploadIntervalMs = 200;     // the texture follows the repaints at most this often
    private const double SlowBakeMs = 5000;         // a bake still running after this long is logged, once

    private static readonly Color HintColor = Palette.UiDim.ToColor();

    private Control _root = null!;
    private MinimapView _corner = null!, _full = null!;
    private Control _fullGroup = null!, _captions = null!;
    private HudBoxes _boxes;

    private Task<(MinimapGround Ground, long Milliseconds)>? _bake;
    private long _builtAt;                      // Stopwatch timestamp of the Build: the log's "ready after"
    private bool _slowReported;
    private MinimapGround? _ground;
    private HeightField? _terrain;              // the map's: a field on another terrain is another map
    private MinimapImage? _image;
    private IReadOnlyList<Prop>? _imageProps;   // the props the picture was painted from
    private Image? _upload;
    private ImageTexture? _texture;
    private bool _dirty;                        // the picture has repaints the texture hasn't had yet
    private int _tilesSinceUpload;
    private ulong _uploadedAt;                  // Time.GetTicksMsec of the last upload
    private ulong _paintedFrame;                // the process frame the picture was painted and first uploaded in
    private MinimapArea _area;
    private string? _mapName;
    private bool _fullOpen, _hidden, _wrongMapReported;

    public MinimapLayer() => Layer = MapLayer;

    public bool FullOpen => _fullOpen;

    /// <summary>
    /// Whether the map's picture is up and has been drawn: uploaded in an earlier frame than this one, so the frame last
    /// rendered — the one a screenshot reads back — has it. Scripted screenshots wait for it.
    /// </summary>
    public bool HasPicture => _texture is not null && _image is not null && Engine.GetProcessFrames() > _paintedFrame;

    public override void _Ready()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _corner = new MinimapView();
        _corner.Setup(full: false);
        _root.AddChild(_corner);

        _fullGroup = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_fullGroup);
        _fullGroup.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = Palette.UiBackground.ToColor(DimAlpha), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fullGroup.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _full = new MinimapView();
        _full.Setup(full: true);
        _fullGroup.AddChild(_full);
        _captions = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _fullGroup.AddChild(_captions);
        _captions.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _captions.Draw += DrawCaptions;

        _root.Resized += Relayout;
        Relayout();
        ApplyVisibility();
    }

    /// <summary>Starts baking <paramref name="field"/>'s ground on a worker thread (the corner shows an empty panel until it is done).</summary>
    /// <param name="notGrassAt">The scenery's bare-ground mask (<c>DirtMask.At</c>), or <c>(_, _) =&gt; 0f</c> for a field without one.</param>
    /// <param name="mapName">The name over the full map; null draws none.</param>
    public void Build(Battlefield field, Func<float, float, float> notGrassAt, string? mapName)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(notGrassAt);
        Reset(field, MinimapArea.Of(field), mapName);
        // Reads only what never changes in a battle (MinimapGround), so the scene may go on with the field meanwhile.
        _bake = Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            MinimapGround ground = MinimapGround.Bake(field, notGrassAt);
            return (ground, clock.ElapsedMilliseconds);
        });
        // Observed from the worker whether or not anyone still waits for it (a scene gone, a Build again): no
        // UnobservedTaskException, and the fault is in the log. GD.PrintErr is thread-safe.
        _bake.ContinueWith(static t => GD.PrintErr($"[minimap] bake failed: {t.Exception!.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>The gallery's: an already baked ground of <paramref name="field"/>, shown at once.</summary>
    public void BuildBaked(MinimapGround ground, Battlefield field, string? mapName)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(field);
        Reset(field, ground.Area, mapName);
        _ground = ground;
        Paint(field);
    }

    /// <summary>Forgets the picture (a bake still running is left to finish unread) for the map of <paramref name="field"/>.</summary>
    private void Reset(Battlefield field, MinimapArea area, string? mapName)
    {
        _bake = null;
        _builtAt = Stopwatch.GetTimestamp();
        _slowReported = false;
        _ground = null;
        _terrain = field.Terrain;
        _image = null;
        _imageProps = null;
        _dirty = false;
        _tilesSinceUpload = 0;
        _wrongMapReported = false;
        _area = area;
        _mapName = mapName;
        _captions?.QueueRedraw();
    }

    /// <summary>Once a frame: the frame to draw; <paramref name="field"/> as the scene has it now (redraws the houses and walls that changed).</summary>
    public void Show(MinimapFrame frame, Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(field);
        TakeBake();
        if (_ground is not null) Repaint(field);
        if (_hidden) return;
        (_fullOpen ? _full : _corner).Show(_image is null ? null : _texture, _area, frame);
    }

    /// <summary>
    /// Brings the picture up to <paramref name="field"/>: painted the first time (or for a field with other props on the
    /// same terrain), else the tiles that changed; the texture follows at most every <see cref="UploadIntervalMs"/>.
    /// </summary>
    private void Repaint(Battlefield field)
    {
        if (!ReferenceEquals(field.Terrain, _terrain))
        {
            if (!_wrongMapReported) GD.PrintErr("[minimap] the field is on another terrain than the map was built for: keeping the old picture");
            _wrongMapReported = true;
            return;
        }
        if (_image is null || !ReferenceEquals(field.Props, _imageProps))
        {
            Paint(field);
            return;
        }
        if (_image.Refresh(field))
        {
            _dirty = true;
            _tilesSinceUpload += _image.LastRepaintedTiles;
        }
        if (!_dirty || Time.GetTicksMsec() - _uploadedAt < UploadIntervalMs) return;
        Upload();
        GD.Print($"[minimap] repainted {_tilesSinceUpload} tiles");
        _tilesSinceUpload = 0;
    }

    /// <summary>Takes the baked ground once the worker is done (a fault the worker has logged leaves the map without a picture); logs a slow bake once.</summary>
    private void TakeBake()
    {
        if (_bake is null) return;
        if (!_bake.IsCompleted)
        {
            if (_slowReported || Stopwatch.GetElapsedTime(_builtAt).TotalMilliseconds < SlowBakeMs) return;
            _slowReported = true;
            GD.Print($"[minimap] still baking after {SlowBakeMs / 1000:0} s");
            return;
        }
        Task<(MinimapGround Ground, long Milliseconds)> bake = _bake;
        _bake = null;
        if (bake.IsCompletedSuccessfully)
        {
            (_ground, long ms) = bake.Result;
            GD.Print($"[minimap] baked {_ground.Pixels} px in {ms} ms (ready after {Stopwatch.GetElapsedTime(_builtAt).TotalMilliseconds:0} ms)");
        }
        else if (bake.IsCanceled)
        {
            GD.PrintErr("[minimap] bake failed: cancelled");
        }
    }

    /// <summary>A new picture of <paramref name="field"/> over the ground, uploaded at once.</summary>
    private void Paint(Battlefield field)
    {
        _image = new MinimapImage(_ground!, field);
        _imageProps = field.Props;
        _tilesSinceUpload = 0;   // the whole picture goes up
        _paintedFrame = Engine.GetProcessFrames();
        Upload();
    }

    /// <summary>The picture into the texture, mipmapped; the texture is made (again) when there is none of this size.</summary>
    private void Upload()
    {
        MinimapImage image = _image!;
        _dirty = false;
        _uploadedAt = Time.GetTicksMsec();
        if (_upload is not null && _upload.GetWidth() == image.Pixels)
        {
            _upload.SetData(image.Pixels, image.Pixels, false, Image.Format.Rgba8, image.Rgba);
            _upload.GenerateMipmaps();
            _texture!.Update(_upload);
            return;
        }
        FreeTexture();
        _upload = Image.CreateFromData(image.Pixels, image.Pixels, false, Image.Format.Rgba8, image.Rgba);
        _upload.GenerateMipmaps();
        _texture = ImageTexture.CreateFromImage(_upload);
    }

    public void ToggleFull()
    {
        _fullOpen = !_fullOpen && !_hidden;
        ApplyVisibility();
    }

    public void CloseFull()
    {
        _fullOpen = false;
        ApplyVisibility();
    }

    /// <summary>Hides both maps (the results screen) or shows the corner one again.</summary>
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (hidden) _fullOpen = false;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (_root is null) return;   // before _Ready: applied there
        _corner.Visible = !_hidden && !_fullOpen;
        _fullGroup.Visible = !_hidden && _fullOpen;
    }

    private void Relayout()
    {
        _boxes = HudLayout.For(new System.Numerics.Vector2(_root.Size.X, _root.Size.Y));
        Place(_corner, _boxes.Minimap);
        Place(_full, _boxes.FullMap);
        _captions.QueueRedraw();
    }

    private static void Place(Control view, in HudRect box)
    {
        view.Position = new Vector2(box.X, box.Y);
        view.Size = new Vector2(box.Width, box.Height);
    }

    private void DrawCaptions()
    {
        HudRect box = _boxes.FullMap;
        float centreX = box.X + box.Width / 2;
        if (!string.IsNullOrEmpty(_mapName))
            HudStyle.DrawOutlinedCentered(_captions, centreX, HudStyle.Baseline(box.Y + NameInset, NameSize), _mapName, NameSize, HudStyle.Text, CaptionOutline);
        float hintMiddle = box.Y + box.Height + HudLayout.FullMapHintHeight / 2;
        HudStyle.DrawOutlinedCentered(_captions, centreX, HudStyle.Baseline(hintMiddle, HintSize), Hint, HintSize, HintColor, CaptionOutline);
    }

    /// <summary>Frees the texture and its image (the views let go of it first); a bake still running is not waited for (its result goes unread).</summary>
    public override void _ExitTree()
    {
        _corner.DropTexture();
        _full.DropTexture();
        FreeTexture();
        (_image, _imageProps) = (null, null);   // back in the tree, the next Show paints and uploads again
    }

    private void FreeTexture()
    {
        _texture?.Dispose();
        _upload?.Dispose();
        (_texture, _upload) = (null, null);
    }
}
