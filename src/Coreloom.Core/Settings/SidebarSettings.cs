namespace Coreloom.Core.Settings;

/// <summary>What the sidebar is made of (Settings › Appearance › Sidebar, since 1.9.2).</summary>
public enum SidebarSurface
{
    /// <summary>One opaque colour, the theme's card colour: nothing of the background shows.</summary>
    Solid,

    /// <summary>Clear glass that bends the light: lightly frosted, a thin tint, a bright rim and a strong highlight.</summary>
    LiquidGlass,

    /// <summary>Frosted glass (the sidebar before 1.9.2): the background blurred behind a dark, nearly opaque tint.</summary>
    FrostedGlass,

    /// <summary>A see-through tint with no blur: the background's colours or picture show through, sharp.</summary>
    Translucent,
}

/// <summary>
/// The sidebar's look: what it is made of, how strong its drop shadow is and how much the glass blurs what is behind it.
/// The defaults are the sidebar as it was before these settings existed.
/// </summary>
public sealed class SidebarSettings
{
    /// <summary>The shadow and blur sliders' middle: the strength the sidebar had before 1.9.2.</summary>
    public const int Standard = 50;

    public SidebarSurface Surface { get; set; } = SidebarSurface.FrostedGlass;

    /// <summary>The drop shadow, in percent (0 none … 50 as before … 100 twice as deep).</summary>
    public int Shadow { get; set; } = Standard;

    /// <summary>How much the glass blurs what is behind it, in percent (0 barely … 50 as much as the cards … 100 twice).</summary>
    public int Blur { get; set; } = Standard;

    /// <summary>The shadow's depth for the sidebar's surface (0 … 2; 1 is the shadow before 1.9.2).</summary>
    public double ShadowDepth => Math.Clamp(Shadow, 0, 100) / (double)Standard;

    /// <summary>The glass blurs (liquid or frosted); a solid or translucent sidebar has nothing to blur.</summary>
    public bool Blurs => Surface is SidebarSurface.LiquidGlass or SidebarSurface.FrostedGlass;

    /// <summary>Liquid glass is clearer than frosted glass: it blurs this much of what frosted glass would.</summary>
    public const double LiquidClarity = 0.45;

    /// <summary>
    /// The sidebar's blur in points, from the cards' (<paramref name="cardFrostPoints"/>): frosted glass the same at 50 %,
    /// scaling to twice at 100 % and down to a light 4 points at 0 %; liquid glass a little under half of that.
    /// </summary>
    public double FrostPoints(double cardFrostPoints) =>
        Math.Max(4, cardFrostPoints * Math.Clamp(Blur, 0, 100) / Standard * (Surface == SidebarSurface.LiquidGlass ? LiquidClarity : 1));

    /// <summary>The sidebar needs its own frosted copy of the background (its blur differs from the cards').</summary>
    public bool NeedsOwnFrost => Surface == SidebarSurface.LiquidGlass || (Blurs && Math.Clamp(Blur, 0, 100) != Standard);

    internal void Normalize()
    {
        if (!Enum.IsDefined(Surface))
        {
            Surface = SidebarSurface.FrostedGlass;
        }

        Shadow = Math.Clamp(Shadow, 0, 100);
        Blur = Math.Clamp(Blur, 0, 100);
    }
}
