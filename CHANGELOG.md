# Changelog

## 0.5.2

- With **Roll to unlock upgrades** on, the shop keeps showing which night upgrades unlocked on. Before, the line under the title went blank after the night they unlocked.

## 0.5.1

- The shop's "Place items myself" tick box shows its label again. It had shrunk to just the tick.
- While placing items yourself, the on-screen panel's "Left to place" now counts the item in your hands too.

## 0.5.0

- New "Place items myself" tick box above the Purchase button. When it's ticked, appliances you buy are put straight into your hands one at a time instead of arriving as parcels, so you can set each one down where you want it. A panel at the bottom of the screen shows what's left, and your third-action button (Y / Triangle on a controller) swaps to the next kind of item. The shop can send whatever's left to the door as parcels. Parcels are still the default.
- Settings can be changed in game with the Mod List mod (0.2.0 or newer): Pause â†’ Mods â†’ the gear next to Blueprint Shop. Without Mod List, they're in `BlueprintShop.cfg` as before.
- New settings:
  - **Upgrades unlock on:** the first night upgrades are on sale. Default: night 5, as in the base game.
  - **Roll to unlock upgrades:** instead of a fixed night, each night has a growing chance to unlock upgrades (the **Upgrade chance per night** setting, default 10%: 10% on night 1, 20% on night 2 and so on). The shop shows tonight's result and next night's chance. Off by default.
  - Once upgrades are unlocked, they stay unlocked for the rest of the run.
  - **Price multiplier:** 0% to 1000%. Default: 100%; 0% makes everything free.
  - **Sale loss:** how much of the price paid selling loses. Default: 0%.
  - **Refund items left outside:** on by default; turn it off for the base game's behaviour.
  - **Your own preferences:** whether **Place items myself** starts ticked, whether the floating sale prices are shown, and the shop menu's controller aim assist and cursor speed.
- **Changed defaults:** decoration nights now come every 5th night, and normal items aren't sold on those nights, as in the base game. If you've played an earlier version, your config file keeps the values it already has (every 3rd night, normal items on), so change them if you want the base game's schedule.
- The Shop terminal has its own hologram: a shopping cart instead of the reroll dice.
- All players in an online game need 0.5.0; it can't be mixed with 0.4.x.

## 0.4.1

- Floating prices over items outside are easier to read: they sit above the bin icon instead of on it, use a dark backing instead of a thick outline, and stay within their own tile, so neighbouring prices no longer overlap. The coin icon is gold instead of white.
- Floating prices grow when you zoom the camera out, so they stay readable. Zoomed in, they keep their normal size.

## 0.4.0

- Sell in bulk: put things outside (where the game shows its bin icon), then sell them all from the shop's new "Items outside" panel. The money arrives straight away.
- Things you bought get a floating price over them while they're outside.
- Anything you bought and leave outside is refunded at the start of the day instead of being thrown away for nothing.
- You can't sell the last appliance your menu needs (for example your only sink), whether you're holding it or it's outside. Its label says "Needed".
- All players in an online game need 0.4.0; it can't be mixed with 0.3.x.

## 0.3.2

- Build mode with a mouse: the crane no longer follows your mouse around behind the shop window.
- The click or button press that closes the shop no longer also reaches the game (for example making the build-mode crane pick something up).
- Right-clicking the search box clears it and starts a fresh search.

## 0.3.1

- Your chef no longer walks around behind the shop window when you move the stick or press movement keys (including typing in search).
- The shop now marks its player as "in a menu", the same way the game's own menus do, so other mods that check for this leave that player's input alone.
- While another menu takes over the game's input (for example a second local player's pause menu), the shop window behind it ignores input.

## 0.3.0 (first public release)

- A shop terminal replaces the reroll button at night. Every blueprint the game could offer tonight is listed, in any quantity.
- Category tabs, search (Ctrl+F; on-screen keyboard on controllers; Ctrl+A or Y / Triangle to clear), owned counts and blueprint tooltips.
- Paid items arrive as parcels; a Blueprint button instead drops a normal blueprint (bought as usual) for cabinets and desks.
- Selling: refunds exactly what you paid.
- Mouse, or a controller cursor with aim assist. Local and online co-op.
- Decoration nights every 3rd night by default, with normal items still on sale (configurable; vanilla is every 5th night, decorations only).
- Wallpaper and floor previews on decoration nights.
- `prepare_uninstall` setting to make a run loadable without the mod.
