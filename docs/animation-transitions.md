# Complete animation transitions

Finish the active video before starting another clip, including clicks, gestures,
drag feedback and farewell. Keep only the latest pending response so repeated
input does not create a long backlog. Farewell takes priority and locks out input.
Show response text when its video starts. Window dragging remains immediate.
Discard pending suspended feedback on release; never play it after put-down.
At a boundary, pending interaction takes precedence over automatic selection and
low-power delay. Keep existing selection weights and disabled clips unchanged.
