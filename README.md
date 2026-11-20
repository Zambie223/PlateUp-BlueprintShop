# Blueprint Shop

A PlateUp! mod that replaces the random nightly blueprint letters with a shop. Every blueprint the game could offer you tonight is listed at once, and you can buy as many of each as you can afford.

The goal is to remove the luck of which blueprints show up. By default, prices, discounts, what's eligible for your restaurant, the decoration schedule and when upgrades unlock all follow the base game's rules. The one change on by default is that bought items you leave outside are refunded at the start of the day, instead of thrown away. If you want a different game, [settings](#settings) change the schedule, prices and how much selling gives back, in game or in a config file.

## Installing

1. Subscribe to the **Harmony** mod on the Steam Workshop (id `2898033283`) and launch PlateUp once so it downloads.
2. Download `BlueprintShop-<version>.zip` from this repo's [Releases](../../releases) page.
3. Extract it into PlateUp's `Mods` folder, so you end up with `Mods\BlueprintShop\BlueprintShop.dll`.
   The `Mods` folder sits next to `PlateUp.exe`, by default `C:\Program Files (x86)\Steam\steamapps\common\PlateUp\PlateUp\Mods`. Create it if it doesn't exist.
4. Start the game.

**Online play: every player needs this mod, the same version.** Players without it can't display the shop and may run into errors.

To update, replace the files in `Mods\BlueprintShop\`. To remove the mod, see [Saves and uninstalling](#saves-and-uninstalling) first.

### Recommended: Mod List

Blueprint Shop works fine on its own, but installing [Mod List](https://github.com/Zambie223/PlateUp-ModList) (version 0.2.0 or newer) alongside it is recommended. It's a separate, optional mod by the same author, installed the same way. Why:

- **Change settings in game.** Without Mod List, the [settings](#settings) are lines in a text file tucked away in your AppData folder, and changes only take effect after restarting the game. With it, they're under **Pause → Mods**, and changes apply straight away, even halfway through a night.
- **Harder to get wrong.** Numbers have sliders and can be typed in, values stay within their allowed range, only the upgrade setting that applies is shown, and there's a **Reset to defaults** button. In the config file, a typo is quietly replaced with the default, with only a warning in the log to tell you.
- **Clear host settings online.** In someone else's game, the host's settings are shown locked, so it's obvious which ones you can change.
- **Check versions.** Mod List shows every installed mod and its version from the pause menu, which makes it easy to confirm that everyone in an online game has the same version of Blueprint Shop.

## How it works

- **Opening the shop:** at night, the reroll button outside your front door becomes a **Shop** terminal. Press interact on it to open the menu. On the very first night (day 0), the terminal appears as soon as you have money, for example in Turbo mode.
- **Browsing:** items are grouped by what they do (Cooking, Automation, Plumbing, Front of House and so on). Upgrades sit in the same tab as the appliance they upgrade, and "Staple", "Upgrade" and "One only" show as badges on the card. Each card also shows how many you already own. Hover over an item to see the same details as its blueprint tooltip.
- **Searching:** type in the search box above the tabs (click it, or press Ctrl+F). On a controller, selecting the search box opens an on-screen keyboard.
- **Buying:** add items to your cart and press **Purchase**. Paid items arrive as parcels on the letter tiles by the door, like your starting appliances. The top right shows your balance and what you'll have left after the cart. Your cart is remembered if you close the menu, until the day starts.
- **Placing items yourself:** tick **Place items myself** above the Purchase button, and appliances skip the parcels. When you close the shop, the first one is put in your hands. Set it down with Grab, as you would when moving any appliance, and the next one appears. A panel at the bottom of the screen shows what you're holding and what's left. Tap your third-action button (Y / Triangle on a controller, or whatever you've bound it to) to swap to the next kind of item. Hold it for a second and a half to take back the item you placed last: what you're holding goes back on the list, and the placed item goes back in your hands. Hold it again to take back the one before that, and so on. Rotate items after placing them, as usual. If you change your mind, the shop's **Send them to the door instead** button turns whatever's left into normal parcels. The day can't start until everything has been placed or sent to the door. The tick box is remembered until you close the game; the **Place items myself** [setting](#settings) decides whether it starts ticked. Decorations are still delivered inside, as usual.
- **Copying from the floor:** press **Copy from floor** in the cart to add everything standing in an area to your cart, for example to build a second copy of a cooking line. The shop hides, and you draw a box over the floor: drag with the mouse, or on a controller move the cursor to one corner, press Interact, move to the opposite corner and press Interact again. The selected tiles light up, and a panel counts what's in them as you go. Esc, right-click or Back returns to the shop without copying.
- **Wishlists:** press **Wishlists** in the cart to save what's in your cart as a named list, and to add a saved list back to the cart on any night, in any run. Lists are kept on your computer, not in the run's save. A list can be replaced with the current cart, or deleted (both ask you to click twice). The [settings](#settings) can export your lists to the clipboard, to back them up or send them to a friend, and import lists from it.
- **Copied items and wishlists** are added on top of what's already in your cart. Anything that can't be bought tonight is left out, and the shop lists what was skipped and why: for example, upgrades that are still locked, decorations on a normal night, items the game never sells as blueprints, or a second "only one allowed" item.
- **Blueprints:** each item also has a **Blueprint** button. It drops a normal, unpaid blueprint outside, for a blueprint cabinet or a research, copying or discount desk.
- **Selling:** pick up an appliance (or an unopened parcel), open the shop, and use **Sell** at the top of the cart. You get back exactly what you paid for it: the discounted price if you bought it on discount, and nothing for things you got for free (starting kit, card rewards). The **Sale loss** [setting](#settings) can make selling give back less. The button asks you to click twice, because selling removes the appliance.
- **Selling in bulk:** put everything you want to sell outside, where the game shows its bin icon. Each item you bought through the shop gets a floating price showing its refund. The shop then lists those items under **Items outside**, and one button (click twice) sells them all at once. The money is yours straight away, so you can spend it the same night. Anything you bought and forget to sell is refunded automatically at the start of the next day, instead of being thrown away for nothing (a [setting](#settings) turns this off). Free items aren't included; the game throws them away as usual.
- **Things your menu needs:** you can't sell the last appliance that does a job your dishes require (for example your only sink), whether you're holding it or it's outside. Its floating label says **Needed** instead of a price. This is the same check the game uses when it warns you before the day starts.
- **Decoration nights** (every 5th night, as in the base game): the shop lists every eligible decoration and event item, plus every wallpaper and floor (free). As in the base game, normal appliances aren't on sale those nights. Both can be changed in the [settings](#settings).

### Controls

| | Keyboard and mouse | Controller |
|---|---|---|
| Point | Mouse | Left stick moves a cursor, which snaps to the nearest button (the shop menu's aim assist and cursor speed are [settings](#settings)) |
| Select | Left click | Interact, Grab or Menu Select |
| Remove one from cart | Right-click a card | Use the "-" button in the cart |
| Search | Click the search box or Ctrl+F, then type | Select the search box for the on-screen keyboard |
| Clear search | Ctrl+A, right-click the search box, or the Clear button | Y / Triangle (your "third action" button if you've rebound it), or the Clear button |
| Scroll | Mouse wheel | Hold the cursor near the top or bottom of the list |
| Close | Esc (first Esc leaves the search box) | Back or Start (first press closes the keyboard) |
| Next item to place (outside the shop) | Tap your third-action key | Tap Y / Triangle (your third-action button) |
| Take back the last placed item | Hold your third-action key for 1.5 seconds | Hold Y / Triangle for 1.5 seconds |
| Copy from floor: draw the box | Drag with the left mouse button | Move the cursor, press Interact on one corner and again on the other |
| Copy from floor: cancel | Esc or right-click | Back (the first press only drops the first corner) |

Your chef stands still while the menu is open. Other players keep playing. In local co-op, each player who opens the shop gets their own half or quarter of the screen.

## Rules it keeps from the base game

These are the defaults. The [settings](#settings) can change the upgrade night, the decoration schedule and prices.

- **What's offered:** the game's own shop pool. Items that need a specific dish, theme or appliance, "only one allowed" items you already own, and staples all work as in vanilla.
- **Upgraded appliances:** available on any night the base game could offer them, which is night 5 onward, or earlier if a card raises the upgrade chance. The base game only gives each letter a chance at an upgrade from then on, while the shop lists them all; the [settings](#settings) can make unlocking them a nightly roll instead. With the base game's decoration schedule, night 5 sells only decorations, so upgrades first show up on night 6.
- **Prices:** exactly the blueprint price: the item's price tier, times every active discount. That includes discount cards, restaurant settings such as Turbo, and the Lunar New Year event. With the "random prices" card, each item gets one random price per night instead of one per letter.
- **"Only one allowed" items:** limited to one per order, and blocked while one is still waiting in a parcel or as a blueprint.
- **Free bonus blueprints from cards** still arrive as free random letters, as in vanilla. They're a free bonus, not a purchase.
- **Rerolling:** gone, since there is nothing left to reroll.

## Settings

There are two ways to change the settings:

- **In game (recommended):** install the [Mod List](https://github.com/Zambie223/PlateUp-ModList) mod (version 0.2.0 or newer; see [why](#recommended-mod-list)), then go to **Pause → Mods** and select the gear next to Blueprint Shop. Changes apply straight away.
- **In the config file:** edit `BlueprintShop.cfg` (see [Troubleshooting](#troubleshooting) for where it is) and restart the game. The mod creates the file on first run, and adds new settings to it when you update.

Settings marked *host* are about how the game plays. In online games, only the host's values count, and other players' in-game pickers are locked. The rest are each player's own and only change what that player sees.

| Setting (in game) | Config key | Default | What it does |
|---|---|---|---|
| **Shop rules** | | | |
| Decoration nights (host) | `decor_interval` | `5` (base game) | Decorations, wallpapers and floors are on sale every this many nights, from 1 to 20. |
| Normal items on decoration nights (host) | `normal_items_on_decor_nights` | `false` (base game) | Decoration nights also sell normal appliances. |
| Refund items left outside (host) | `refund_outside_at_day_start` | `true` | Bought items still outside when the day starts are sold automatically. `false` gives the base game's behaviour: they're thrown away for nothing. |
| **Balance** | | | |
| Price multiplier (host) | `price_percent` | `100` (base game) | Applies to every shop price, on top of the game's own discounts, from 0% to 1000%. 0% makes everything free. |
| Sale loss (host) | `sale_loss_percent` | `0` | How much of the price paid you lose when selling, from 0% to 100%. This includes selling everything outside and the day-start refund. 0% gives back the full price. |
| Roll to unlock upgrades (host) | `upgrade_roll` | `false` | Off: upgraded appliances unlock on a fixed night (below). On: each night rolls a growing chance to unlock them instead. |
| Upgrades unlock on (host, roll off) | `upgrade_night` | `5` (base game) | The first night upgrades are on sale, from 1 to 30. Cards that raise the upgrade chance still unlock them earlier. |
| Upgrade chance per night (host, roll on) | `upgrade_chance_per_night` | `10` | How much the unlock chance grows each night, from 1% to 100%: at 10%, night 1 has a 10% chance, night 2 has 20%, and so on. Cards that raise the upgrade chance add to it. |
| **Your preferences** | | | |
| Place items myself | `place_items_myself` | `false` | Whether the shop's **Place items myself** box starts ticked. |
| Floating sale prices | `show_sale_labels` | `true` | Shows the refund prices above bought items outside. |
| Shop menu aim assist | `shop_aim_assist` | `Normal` | Controllers only: how strongly the shop menu's cursor snaps to nearby buttons (`Off`, `Low`, `Normal` or `High`). It doesn't affect gameplay. |
| Shop menu cursor speed | `shop_cursor_speed_percent` | `100` | Controllers only: how fast the shop menu's cursor moves, from 50% to 200%. |
| **Wishlists** (in game only) | | | |
| Export wishlists | | | Copies all your wishlists to the clipboard as text. |
| Import wishlists | | | Adds the wishlists in the clipboard to yours. If you already have a list with the same name and different items, the new one gets a number added. |
| **Troubleshooting** | | | |
| Verbose logging | `verbose` | `false` | See [Troubleshooting](#troubleshooting). |
| Prepare to uninstall (host) | `prepare_uninstall` | `false` | See [Saves and uninstalling](#saves-and-uninstalling). |

In game, the price, sale loss and upgrade chance settings have a slider, and you can click the number (or press confirm on it) to type a value. Holding left or right on any number speeds it up. Only the upgrade setting that applies is shown: the night when rolling is off, the chance when it's on.

**Upgrades** stay unlocked for the rest of the run once they've been on sale, even if you change these settings later. With rolling on, the shop's title shows tonight's result and next night's chance. Each night's roll is fixed for the run, so reopening the shop or reloading the night's save doesn't roll again. For a sense of scale: at 10% per night, about half of runs unlock upgrades by night 3. At 3% per night, it's about night 6 or 7, closer to the base game. In the base game, from night 5 each blueprint letter has a 10% chance (20% from night 10, and so on) to let upgrades into its draw, so the first upgrade often shows up several nights after night 5.

The in-game page also has a **Reset to defaults** button for the shop rules, balance and your preferences. In someone else's game, it only resets your preferences.

Prices are what you actually pay, so refunds always start from the price you paid, whatever the multiplier was at the time. Changing a shop rule or the price multiplier during a night updates tonight's shop straight away. Anything that's no longer for sale is taken out of carts.

Upgrading from an older version: older versions wrote `decor_interval=3` and `normal_items_on_decor_nights=true` into the config file, and those values are kept. To switch to the base game's schedule, change them in game or in the file.

## Saves and uninstalling

The shop itself is never saved. The mod adds two small records to save files: a "price paid" record on each appliance you buy, so selling can refund the right amount on later nights, and one record saying whether upgrades have been unlocked in this run.

Because of those records, **a run that was saved with the mod can't be loaded without it.** Your franchise, other runs, and new runs are unaffected. To finish a run without the mod:

1. Turn on **Prepare to uninstall** in the in-game settings, or set `prepare_uninstall=true` in `BlueprintShop.cfg`. In an online game, the host does this.
2. Load the run and play into the next night. The game autosaves at the start of each night, and the mod removes all its records before then.
3. Remove the mod. The run now loads without it.

While `prepare_uninstall` is on, purchases aren't recorded and refunds are $0. It removes the existing records as soon as it's turned on, and turning it off again doesn't bring them back.

## Troubleshooting

Verbose logging is off by default. To turn it on, do any of these:

- Turn on **Verbose logging** in the in-game [settings](#settings).
- Add the launch option `-blueprintshop-verbose` (Steam → PlateUp → Properties → Launch Options). This overrides the setting.
- Set `verbose=true` in `BlueprintShop.cfg`. The mod creates this file on first run, next to the game's log at `%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\`.

Wishlists are saved in `BlueprintShop-wishlists.txt` in the same folder. It's plain text, in the same format the export puts on the clipboard, so you can also back it up or copy it to another computer.

If the menu text ever shows up as symbols, add `font=<name>` to `BlueprintShop.cfg`. With verbose logging on, the log lists every font name the game has loaded.

Logs go to `Player.log` in the same folder. Every line from this mod starts with `[Blueprint Shop]`. Please include it when reporting a bug.

## Known limitations

- The terminal reuses the reroll button's model, relabelled "Shop", with a shopping-cart hologram in place of the dice.
- Wallpaper and floor names come from the game's internal asset names, because the game has no display names for them.
- If the letter area by the door is full, an order is refused until you open or move some parcels. Nothing is ever stacked on one tile.

## Building from source

You need:

- The [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer. The mod targets `netstandard2.0`, C# 9.
- PlateUp! installed through Steam, and the Harmony Workshop item subscribed. The project compiles against the game's own DLLs and Harmony's `0Harmony.dll` on your machine; none of them are in this repo, and none are copied into the build output.

Then run:

```
dotnet build -c Release
```

The build copies `BlueprintShop.dll` and `.pdb` into the game's `Mods\BlueprintShop\` folder. Restart the game to load a new build. Add `-p:DeployMod=false` to build without copying. To remove the local copy again: `dotnet msbuild -t:UndeployFromPlateUp`.

If PlateUp isn't in the default Steam library, either set the `PLATEUP_GAME_DIR` environment variable to the folder containing `PlateUp.exe`, or create `mods\Directory.Build.local.props` (git-ignored):

```xml
<Project><PropertyGroup>
  <PlateUpGameDir>D:\SteamLibrary\steamapps\common\PlateUp\PlateUp</PlateUpGameDir>
</PropertyGroup></Project>
```

## License

The source code in this repository is under the [MIT License](LICENSE). PlateUp! and its code and assets belong to their respective owners and aren't included here.

---

### Responsible AI use disclosure

This mod was written with the assistance of an AI coding tool (Claude, by Anthropic), under the direction of the author (Zambie). Its behavior is based on the game's own code, which was read to match the base game's shop rules, rather than on AI recall. It is play-tested in-game, including online co-op, before each release.
