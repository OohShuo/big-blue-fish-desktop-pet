namespace BigBlueFish;

internal enum PetGesture { Shake, HeadPet, PokedSurprised, PokedAngry, LongPress, HeldAloft, TailTickle, Startled, Soothe }
internal readonly record struct GesturePoint(double X, double Y)
{
    internal double Distance(GesturePoint other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}

// Pure input recognizer: positions use the 640×360 sprite coordinate system;
// timestamps are monotonic milliseconds. No UI, timers or screen capture here.
internal sealed class PetGestureRecognizer(Func<bool>? startleChance = null)
{
    readonly Func<bool> chance = startleChance ?? (() => Random.Shared.Next(3) == 0);
    readonly Dictionary<PetGesture, long> cooldowns = new();
    readonly Queue<long> clicks = new();
    GesturePoint pressOrigin, dragSample, stationaryAnchor, dragDirection, lastHover, strokeDirection;
    long pressedAt, dragAt, movedAt, shakeAt, hoverAt, strokeAt, hoverBlockedUntil, upsetUntil;
    bool pressed, moved, lifted, pressHandled, shakeFired, heldFired, hasHover;
    int shakeReversals, strokeReversals, pokeStage;
    double strokeDistance;
    string strokeRegion = "";

    internal bool IsUpset(long now) => now < upsetUntil;
    internal void Press(GesturePoint point, long now)
    {
        pressed = true; moved = lifted = pressHandled = shakeFired = heldFired = false;
        pressOrigin = dragSample = stationaryAnchor = point; pressedAt = dragAt = movedAt = now;
        dragDirection = default; shakeReversals = 0; shakeAt = now;
        ResetHover();
    }
    internal PetGesture? MoveDrag(GesturePoint point, long now, bool isDragging)
    {
        if (!pressed) return null;
        lifted |= isDragging;
        moved |= point.Distance(pressOrigin) > 6;
        if (point.Distance(stationaryAnchor) >= 3) { movedAt = now; stationaryAnchor = point; }
        var delta = Delta(point, dragSample);
        double distance = point.Distance(dragSample);
        long elapsed = now - dragAt;
        if (!lifted || shakeFired) return null;
        // Accumulate tiny events: 1px at 1000Hz is still a fast movement.
        if (distance < 20)
        {
            if (elapsed > 100) { dragSample = point; dragAt = now; }
            return null;
        }
        double speed = distance * 1000 / Math.Max(1, elapsed);
        dragSample = point; dragAt = now;
        if (speed < 900 || elapsed > 250) { shakeReversals = 0; dragDirection = default; shakeAt = now; return null; }
        if (now - shakeAt > 900) { shakeAt = now; shakeReversals = 0; dragDirection = default; }
        if (Opposite(delta, dragDirection)) shakeReversals++;
        dragDirection = delta;
        if (shakeReversals < 3) return null;
        shakeFired = pressHandled = true;
        return Emit(PetGesture.Shake, now);
    }
    internal PetGesture? Hold(long now)
    {
        if (!pressed) return null;
        if (!lifted && !moved && !pressHandled && now - pressedAt >= 2000)
        {
            pressHandled = true; clicks.Clear(); pokeStage = 0;
            return Emit(PetGesture.LongPress, now);
        }
        if (lifted && !heldFired && !shakeFired && now - pressedAt >= 4000 && now - movedAt >= 2500)
        {
            heldFired = true;
            return Emit(PetGesture.HeldAloft, now);
        }
        return null;
    }
    internal PetGesture? Release(long now, bool wasDragged, out bool suppressClick)
    {
        suppressClick = pressHandled || wasDragged;
        if (!pressed) return null;
        pressed = false; ResetHover();
        if (wasDragged || lifted || pressHandled) { clicks.Clear(); pokeStage = 0; return null; }
        while (clicks.Count > 0 && now - clicks.Peek() > 2000) clicks.Dequeue();
        if (clicks.Count == 0) pokeStage = 0;
        clicks.Enqueue(now);
        PetGesture? result = null;
        if (clicks.Count >= 7 && pokeStage < 2) { pokeStage = 2; result = Emit(PetGesture.PokedAngry, now); }
        else if (clicks.Count >= 4 && pokeStage < 1) { pokeStage = 1; result = Emit(PetGesture.PokedSurprised, now); }
        suppressClick = result != null;
        return result;
    }
    internal void CancelPress()
    {
        pressed = false; pressHandled = false; clicks.Clear(); pokeStage = 0;
        ResetHover();
    }
    internal void ResetHover() { hasHover = false; ResetStroke(); }
    void ResetStroke() { strokeRegion = ""; strokeAt = 0; strokeDistance = 0; strokeReversals = 0; strokeDirection = default; }
    internal PetGesture? Hover(GesturePoint point, long now)
    {
        if (pressed) { ResetHover(); return null; }
        if (!hasHover) { lastHover = point; hoverAt = now; hasHover = true; return null; }
        var previous = lastHover; long elapsed = now - hoverAt;
        lastHover = point; hoverAt = now;
        if (elapsed <= 0 || elapsed > 250 || now < hoverBlockedUntil) { ResetStroke(); return null; }
        double distance = point.Distance(previous), speed = distance * 1000 / elapsed;
        if (!InBody(previous) && InBody(point) && speed >= 1300 && chance())
        {
            ResetStroke(); return Emit(PetGesture.Startled, now);
        }
        string region = InHead(point) && InHead(previous) ? "head" : InTail(point) && InTail(previous) ? "tail" : "";
        bool suitable = region == "head" ? speed is >= 50 and <= 550 : region == "tail" && speed is >= 300 and <= 2000 && distance <= 60;
        if (!suitable)
        {
            // A brief pause at a stroke's turning point is normal.
            if (region.Length > 0 && strokeRegion == region && now - strokeAt <= 1500 && speed < (region == "head" ? 50 : 300)) return null;
            ResetStroke(); return null;
        }
        if (strokeRegion != region || now - strokeAt > 1500)
        {
            ResetStroke(); strokeRegion = region; strokeAt = now;
        }
        var direction = Delta(point, previous);
        if (Opposite(direction, strokeDirection)) strokeReversals++;
        strokeDirection = direction; strokeDistance += distance;
        if (region == "head" && now - strokeAt >= 600 && strokeReversals >= 2 && strokeDistance >= 90)
        {
            ResetStroke(); return Emit(IsUpset(now) ? PetGesture.Soothe : PetGesture.HeadPet, now);
        }
        if (region == "tail" && now - strokeAt <= 800 && strokeReversals >= 3 && strokeDistance >= 50)
        {
            ResetStroke(); return Emit(PetGesture.TailTickle, now);
        }
        return null;
    }
    PetGesture? Emit(PetGesture kind, long now)
    {
        if (cooldowns.TryGetValue(kind, out var until) && now < until) return null;
        cooldowns[kind] = now + (kind == PetGesture.Startled ? 15000 : 3000);
        hoverBlockedUntil = now + 2500;
        if (kind is PetGesture.Shake or PetGesture.PokedAngry) upsetUntil = now + 20000;
        if (kind == PetGesture.Soothe) upsetUntil = 0;
        return kind;
    }
    static GesturePoint Delta(GesturePoint point, GesturePoint previous) => new(point.X - previous.X, point.Y - previous.Y);
    static bool Opposite(GesturePoint a, GesturePoint b) => a.X * b.X + a.Y * b.Y < -.35 * Math.Sqrt((a.X * a.X + a.Y * a.Y) * (b.X * b.X + b.Y * b.Y));
    static bool InBody(GesturePoint p) => p.X is >= 208 and <= 450 && p.Y is >= 58 and <= 336;
    static bool InHead(GesturePoint p) => p.X is >= 245 and <= 390 && p.Y is >= 70 and <= 190;
    static bool InTail(GesturePoint p) => p.X is >= 365 and <= 460 && p.Y is >= 210 and <= 330;
}
