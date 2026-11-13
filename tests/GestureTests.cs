using BigBlueFish;

internal static class GestureTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); count++; }
        PetGestureRecognizer New() => new(() => true);
        var r = New();
        r.Press(new(0, 0), 0);
        Check(r.Hold(1999) == null, "Long press must wait");
        Check(r.Hold(2000) == PetGesture.LongPress, "Stationary long press");
        Check(r.Hold(4000) == null, "Long press fires only once");
        r.Release(4100, false, out var suppress);
        Check(suppress, "Long press release is not a normal click");

        r = New(); r.Press(new(0, 0), 0);
        Check(r.MoveDrag(new(100, 0), 100, true) == null, "One fast move is not shaking");
        Check(r.MoveDrag(new(0, 0), 200, true) == null, "One reversal is not shaking");
        Check(r.MoveDrag(new(100, 0), 300, true) == null, "Two reversals are not shaking");
        Check(r.MoveDrag(new(0, 0), 400, true) == PetGesture.Shake, "Rapid reversals shake");
        Check(r.MoveDrag(new(100, 0), 500, true) == null, "Only one shake per lift");
        r.Release(600, true, out _);
        r = New(); r.Press(new(0, 0), 0);
        bool fineShake = false;
        for (int i = 1; i <= 400; i++)
        {
            int leg = (i - 1) / 100, offset = (i - 1) % 100 + 1;
            double x = leg % 2 == 0 ? offset : 100 - offset;
            fineShake |= r.MoveDrag(new(x, 0), i, true) == PetGesture.Shake;
        }
        Check(fineShake, "High-frequency small mouse events still recognize shaking");

        r = New(); r.Press(new(0, 0), 0); r.MoveDrag(new(50, 0), 200, true);
        Check(r.Hold(2500) == null, "Lifted notice waits for a stationary hold");
        Check(r.Hold(4200) == PetGesture.HeldAloft, "Held aloft notice");
        Check(r.Hold(7000) == null, "Held aloft notice fires once");
        r.CancelPress(); Check(r.Hold(9000) == null, "Lost capture cancels hold");

        r = New(); PetGesture? feedback = null;
        for (int i = 0; i < 4; i++) { r.Press(new(0, 0), i * 200); feedback = r.Release(i * 200 + 50, false, out _); }
        Check(feedback == PetGesture.PokedSurprised, "Four pokes surprise");
        for (int i = 4; i < 7; i++) { r.Press(new(0, 0), i * 200); feedback = r.Release(i * 200 + 50, false, out _); }
        Check(feedback == PetGesture.PokedAngry, "Continued pokes anger");
        Check(r.IsUpset(1500), "Anger is transient state");
        Check(Stroke(r, 5000) == PetGesture.Soothe, "Head strokes soothe anger");
        Check(!r.IsUpset(6000), "Soothed state clears anger");
        Check(!r.IsUpset(50000), "Anger expires");

        r = New(); Check(Stroke(r, 1000) == PetGesture.HeadPet, "Slow head strokes");
        Check(Stroke(r, 2200) == null, "Hover feedback cooldown");
        r = New();
        r.Hover(new(390, 260), 1000);
        r.Hover(new(420, 260), 1050);
        r.Hover(new(390, 260), 1100);
        r.Hover(new(420, 260), 1150);
        Check(r.Hover(new(390, 260), 1200) == PetGesture.TailTickle, "Tail tickle reversals");
        r = New(); r.Hover(new(100, 200), 1000);
        Check(r.Hover(new(320, 200), 1100) == PetGesture.Startled, "Sudden body entry");
        r.Hover(new(100, 200), 5000);
        Check(r.Hover(new(320, 200), 5100) == null, "Startle has long cooldown");
        r = new(() => false); r.Hover(new(100, 200), 1000);
        Check(r.Hover(new(320, 200), 1100) == null, "Startle is occasional");
        r = New(); r.Press(new(0, 0), 0);
        Check(Stroke(r, 1000) == null, "No hover gestures while pressed");
        r.CancelPress(); Check(r.Release(4000, false, out _) == null, "Canceled press is not a poke");
        Console.WriteLine($"PASS: {count} gesture checks");
        return count;
    }
    static PetGesture? Stroke(PetGestureRecognizer recognizer, long start)
    {
        PetGesture? result = null;
        for (int i = 0; i <= 8; i++)
        {
            var value = recognizer.Hover(new(i % 2 == 0 ? 280 : 310, 120), start + i * 120);
            result ??= value;
        }
        return result;
    }
}
