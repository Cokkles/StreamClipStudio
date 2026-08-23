namespace StreamClipStudio;

internal sealed class WheelSafeNumericUpDown : NumericUpDown
{
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Numeric values in this app are consequential. Require clicks, typing,
        // or the spinner buttons so ordinary page scrolling cannot change them.
    }
}
