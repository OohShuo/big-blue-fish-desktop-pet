using Avalonia;

namespace BigBlueFish;

internal sealed partial class LinuxPetWindow
{
    PetGestureRecognizer gestures = new();
    PetGesture? dragReaction;
    double GestureScale => SpriteSize / 360 * DesktopScaling;
    GesturePoint GestureScreen(PixelPoint point) => new(point.X / GestureScale, point.Y / GestureScale);
    void CancelGestureInput()
    {
        gestures.CancelPress(); dragReaction = null;
        if (pendingPlayback?.Phase != "Exit") pendingPlayback = null;
        if (!dragging) return;
        dragging = false;
        var pointer = capturedPointer; capturedPointer = null;
        pointer?.Capture(null); Clamp(); Save();
    }
    void HandleHeldGesture(PetGesture? gesture)
    {
        if (gesture == null || Puppet || phase == "Exit" || closed) return;
        if (gesture == PetGesture.Shake)
        {
            dragReaction = gesture;
            ShowText(PetGestureResponse.For(gesture.Value).Text);
        }
        else if (gesture == PetGesture.HeldAloft) ShowText(PetGestureResponse.For(gesture.Value).Text);
        else PlayGesture(gesture.Value);
    }
    void PlayGesture(PetGesture gesture)
    {
        if (closed || exitRequested || Puppet || phase == "Exit" || hidden || fullscreenHidden || settings != null) return;
        var feedback = PetGestureResponse.For(gesture);
        if (feedback.Action != null) RequestPlayback(policy.Find(feedback.Action), "Gesture", false, feedback.Text);
        else ShowText(feedback.Text);
    }
    void TickGestures()
    {
        if (exitRequested || Puppet || !Preferences.MouseInteractions || settings != null || ContextMenu?.IsOpen == true || phase == "Exit") { gestures.ResetHover(); return; }
        long now = Environment.TickCount64;
        if (dragging) { HandleHeldGesture(gestures.Hold(now)); return; }
        if (phase == "Startup") { gestures.ResetHover(); return; }
        if (!CompanionNative.TryGetPointer(out var x, out var y, out var buttons) || (buttons & 0x1F00) != 0)
        { gestures.ResetHover(); return; }
        var point = new GesturePoint((x - Position.X) / GestureScale, (y - Position.Y) / GestureScale);
        var gesture = gestures.Hover(point, now);
        if (gesture != null) PlayGesture(gesture.Value);
    }
}
