# Tractor Beam 0.2.2 preview

The Tractor Beam is a solo-play functional block registered by Starship Keel. It uses the stock Repair Beam model, materials, collision, grid sizes and vertical stretch rules. It can slow or retrieve an eligible ship while its pilot retains control.

Target game is **Steam build ID 26451593 / 26w45a**, Unity **6000.6.6f1**, Windows x64 Mono. Requires **Starship Keel v0.1.2** and **BepInEx 5.4.23.5**. The Keel reference archive was verified against the published SHA-256 `1cea74eb7598695893f8c8bf7816044028c1492efcea31ee66ebe64a695d8c07`.

## Find and use it

Run `pwsh -File .\Start-Test.ps1` from this folder to launch the prepared isolated copy. In a disposable solo Creative world, search the Codex for **Tractor Beam**. If the block is missing, check the BepInEx log for Keel's registration result. A normal Steam launch can load the plugin in Keel's audit mode, which skips block registration.

1. Place the Tractor Beam on a powered ship with its nozzle facing the target.
2. Link **Yoke → Event Gate → Tractor Beam** with the game's logic tool. Set the gate to the Yoke's **Primary Fire** signal and **Input Signal** mode. Holding the control enables the beam. Releasing it or disconnecting the signal drops the lock.
3. To brake without towing, set `[Behaviour] Retrieve = false` in the Tractor Beam BepInEx config. The default brakes and retrieves at a requested closing speed up to 6 m/s. The hold distance is the larger of 5 m or the target hull radius plus 3 m.

The rating uses the placed grid size. Vertical stretch moves the muzzle and retains the grid size's force rating.

While the input is held, a pulsing amber beam shows that the emitter is searching or sees an ineligible target. Cyan means it has a valid lock. A short red pulse at the nozzle means the source cannot sustain the beam, for example because it is out of power or cooling. If you see no pulse, check that the gate uses **Input Signal** and that its output reaches the block.

If the beam stays amber, check the latest Tractor Beam entries in `BepInEx/LogOutput.log`. The mod reports when the ray misses, hits a shield or other obstruction, or rejects a target by mass, span, docking or another eligibility rule. A newly built ship's temporary build stasis is eligible. Active antigravity anchor blocks still prevent capture.

| Emitter grid size | Maximum target mass in game units | Maximum hull span | Force limit in game units | Range |
| --- | ---: | ---: | ---: | ---: |
| 0.25 m | 937.5 | 3 m | 1,250 | 45 m |
| 1 m | 15,000 | 12 m | 20,000 | 60 m |
| 4 m | 240,000 | 48 m | 320,000 | 120 m |

These are initial tuning values. Opposing thrust can overcome the capped force. Anchored, fixed, docked and warping targets are excluded. Power draw and heat scale with load. Only one emitter per source can act per frame, and each target receives force from at most one emitter.

## Package and removal

Import the ZIP into a separately isolated Uplink profile. Install Keel v0.1.2 and enable both experimental stock-shape content and functional blocks in that managed profile. The isolated test launcher sets these options for its own process. The ZIP contains only the Tractor Beam plugin and package metadata.

The package and plugin ID remain `local.sevo.tractorbeam`. The functional content key remains `local.sevo.tractorbeam:beam-emitter`, revision 1. Keel reserves the older cube preview's separate `emitter` key. Remove cube emitters using the old preview before migrating a save containing them. Before removing the functional plugin, remove its blocks from any save or blueprint you want to keep. Preserve Keel's registry with the corresponding test saves.

The offline checks cover mass and span rejection, braking, escape under stronger thrust and settling at the hold distance. The native logic probe covers the Repair Beam template contract, placement and sustained Event Gate input. Live towing, power draw, save and blueprint round trips, and multiplayer are outside that probe.
