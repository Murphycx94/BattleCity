namespace Tank.Client.World;

/// <summary>
/// The fade of the trees and houses that get in the way of the view (riverland design §6.4, §6.5; the bushes' rule): an
/// item in the way fades to <see cref="SeeThrough"/> over <see cref="FadeSeconds"/>, and back once clear. A view numbers
/// its items 0 … count − 1 and each frame calls <see cref="BeginFrame"/>, <see cref="MarkInWay"/> for each item in the way
/// (those in the sectors near <see cref="NearCamera"/> of the camera and along the line to the tank, or every item when
/// those are more than <see cref="MaxSectors"/>), then <see cref="Step"/>, which moves every item in the way or still fading towards its
/// opacity and lists those that changed, for the view to upload. Only fading items are stepped; one back to solid and
/// clear drops off the list. Allocates nothing after it is built.
/// </summary>
public sealed class SeeThroughFades
{
    /// <summary>Items within this many metres of the camera fade.</summary>
    public const float NearCamera = 6f;
    /// <summary>The opacity an item in the way fades to.</summary>
    public const float SeeThrough = 0.5f;
    /// <summary>How long a fade from solid to <see cref="SeeThrough"/> (or back) takes, seconds.</summary>
    public const float FadeSeconds = 0.3f;
    /// <summary>
    /// The most sectors a view looks in per frame, around the camera and along the line to the tank (as the bushes'); a
    /// view that spans more looks at every item instead.
    /// </summary>
    public const int MaxSectors = 256;

    private readonly float[] _opacity;
    private readonly int[] _inWayFrame;   // the last frame each item was in the way
    private readonly bool[] _isFading;    // in _fading
    private readonly List<int> _fading;
    private int _frame;

    public SeeThroughFades(int count)
    {
        _opacity = new float[count];
        _inWayFrame = new int[count];
        _isFading = new bool[count];
        _fading = new List<int>(count);
        Array.Fill(_opacity, 1f);
        Array.Fill(_inWayFrame, -1);
    }

    public float OpacityOf(int item) => _opacity[item];

    /// <summary>How many items are fading one way or the other.</summary>
    public int FadingCount => _fading.Count;

    public void BeginFrame() => _frame++;

    /// <summary>Item <paramref name="item"/> is in the way this frame (marking it again changes nothing).</summary>
    public void MarkInWay(int item)
    {
        _inWayFrame[item] = _frame;
        if (_isFading[item]) return;
        _isFading[item] = true;
        _fading.Add(item);
    }

    /// <summary>
    /// Moves each fading item <paramref name="delta"/> seconds' worth towards <see cref="SeeThrough"/> if it was marked
    /// this frame, else towards solid; <paramref name="changed"/> is cleared and given the items whose opacity moved.
    /// </summary>
    public void Step(float delta, List<int> changed)
    {
        changed.Clear();
        float rate = (1f - SeeThrough) * delta / FadeSeconds;
        for (int k = _fading.Count - 1; k >= 0; k--)
        {
            int item = _fading[k];
            float target = _inWayFrame[item] == _frame ? SeeThrough : 1f;
            float opacity = MoveToward(_opacity[item], target, rate);
            if (opacity != _opacity[item])
            {
                _opacity[item] = opacity;
                changed.Add(item);
            }
            if (opacity < 1f || target < 1f) continue;
            _isFading[item] = false;
            _fading.RemoveSwapAt(k);
        }
    }

    /// <summary><paramref name="from"/> moved <paramref name="step"/> towards <paramref name="to"/>, not past it (as Godot's <c>Mathf.MoveToward</c>).</summary>
    private static float MoveToward(float from, float to, float step) =>
        MathF.Abs(to - from) <= step ? to : from + MathF.Sign(to - from) * step;
}
