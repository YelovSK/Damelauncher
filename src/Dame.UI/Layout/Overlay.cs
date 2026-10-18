using System.Drawing;
using Dame.UI.Foundation;

namespace Dame.UI.Layout;

/// <summary>Stacks its children on top of each other, each over the whole overlay.</summary>
internal sealed class Overlay : UiElement
{
    internal Overlay(params UiElement[] children)
    {
        foreach (UiElement child in children)
        {
            AddChild(child);
        }
    }

    protected override bool HitTestCore(PointF position) => false;
}
