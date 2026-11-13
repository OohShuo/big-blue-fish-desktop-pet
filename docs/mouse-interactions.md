# Mouse interactions

Approved: implement all eight proposed interactions with existing enabled clips.
Use a deterministic, model-free recognizer with monotonic time, reference-sprite
coordinates and explicit cooldowns. Keep the four disabled actions untouched.

- Shake: several fast direction reversals while dragged; show a notice in air,
  then an angry animation on release. A single fast relocation is not shaking.
- Head strokes: slow repeated movement across the head, without a held button.
- Repeated pokes: four clicks in two seconds surprise; continued poking angers.
- Long press: two seconds without dragging; suppress the ordinary release click.
- Held aloft: after lifting and holding stationary, ask to be put down once.
- Tail tickles: fast, short, repeated direction changes near the tail.
- Sudden approach: occasional surprise on a rapid entry; long cooldown.
- Soothe: head strokes within a short anger window give an affectionate response.

Integration: preserve pointer capture and dragging; never replace farewell.
Gesture feedback cancels automatic motion, completes its animation, then resumes
normal/low-power playback. Clear unfinished gestures on settings, hide and lost
capture. Pointer position polling is local X11/XWayland only; no screen reading.
Add one settings switch for the new gestures, default on. Persist that switch;
anger and gesture history are transient. Verify recognizer thresholds, cooldowns,
all eight gestures, existing interactions, real decoding and Xvfb UI flows.
