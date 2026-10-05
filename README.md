# FrameHapticsFix

Steam Frame controller haptics are normally too long and inconsistent. This is because:
- They get detected as knuckles controllers, which doubles the duration of every haptics preset
- Beat Saber restarts haptics via device.SendHapticImpulse very often, and the frame controllers
  really don't like that.

This mod attempts to fix both of those. Works on my machine, anyway.

There are no toggles or UI/settings.
If the mod is installed, the patches are
applied. You’ll be able to tell the
difference as soon as you hit a note.

Compatible with Tweaks55 and HapticsTweaker (actually makes their settings work correctly).

*This mod has only been tested with 1.40.8 and 1.44.1.* Based on my observations of 1.45.2 vanilla, it seems likely that this mod will be unnecessary in future versions of Beat Saber (Tweaks55 and HapticsTweaker will be enough on their own).

## Warnings/Notes

- I've never made a beat saber mod before so sorry if my code sucks
- I try to not do anything for other controller types but probably/definitely fail
  - There's no enable/disable toggle so just don't install this if you're not on frame or uninstall when switching
  - I couldn't figure out a way to tell frame controllers from index controllers so this'll definitely mess up index
    haptics
- Cutting a block and following through into a wall might lose the wall haptics sometimes but I kinda had to try
- Rapidly highlighting things in menu is a bit more haptically intense than by default
