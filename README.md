# FrameHapticsFix

**Use of HapticsTweaker (or Tweaks55) is recommended alongside this mod!**

## What's this do?

**TL;DR**: I've cleaned up haptics triggers to the best of my ability, so the Frame controllers should behave more
consistently and actually behave in accordance with configurations by mods that adjust haptics.

---

Steam Frame controller haptics are normally too long and inconsistent. There's a few contributing factors
that I've picked up on at this point:
- The haptics player keeps sending new, changing impulse information to the controller-specific haptics handler
  (the frame controllers get identified as Index Knuckles)
- The Knuckles handlers attempt to double every input duration, but because of where it happens, each update
  chops time off the remaining duration
  - This causes some variability in terms of when haptics will end
  - The Frame controllers also seem to dislike being provided these repeated, decreasing pulse durations

To be honest, the Frame controllers are kind of touchy. They can't be given new haptics pulse information more
than once every 1/80th of a second (and judging by default game behavior, the index controllers didn't like that
either). Feedback can also sometimes behave unexpectedly, I which I *think* comes from the impulse durations
including motor spin-up and spin-down profiles?

*This mod has only been tested with 1.40.8 and 1.44.1.*

## Notes for Haptics Configurations

If using HapticsTweaker, I've noticed a few things:
- Clash and Arc strengths below ~40-50% can dull/mute haptics for notes that happen right afterwards
- Chains seem to respond best to having the times/strengths of the head/links be about the same

## Warnings/Other Notes

- I've never made a beat saber mod before so sorry if my code sucks
- This mod affects Knuckles haptics!
  - From my own testing, it doesn't seem to make them *worse*, but I don't have an enable toggle,
    so if you don't want this mod to change those anymore, you'll need to just remove it
- Rapidly highlighting things in menu is a bit more haptically intense than by default.
  - The fix for this, I think, would be adding configuration options to an existing haptics adjustment mod to
    reduce the intensity or duration of menu highlight triggers. None do this so far.
