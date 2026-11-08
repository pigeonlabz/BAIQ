# Draft release notes — 1.3.0 (unreleased)

**Author: PigeonLabs**

Thank you for 5,000 unique downloads of Inventory Expanded - Backpack &
Inventory QoL. This update expands Increase Production while keeping the MAIN
and BETA builds separate.

- **Increase Production:** Independent settings support drying racks,
  standard/Mk2 mixing stations, and player plant hand-harvests. Each uses the
  selected Stack Size multiplier (2x, 4x, or 8x) only for its own system.
  Mixes consume both ingredients in their native 1:1 ratio. Drying and growth
  time remain native; larger mixer batches use the native-capacity processing
  timeframe. A plant harvest proceeds only if the full multiplied yield fits
  the inventory. Other production workstations, employee/NPC plant
  harvesting, Packaging Stations, and Delivery Capacity are not included.
  Processing and interaction cadence remain native.
- **Packaging Stations:** Deferred from 1.3.0. The incomplete combined Mk1/Mk2
  feature is not exposed in Cheats or active in the public DLLs. Its research
  and candidate safety work remain preserved for a later release.
- **Deliveries:** Purchased quantities remain unchanged. The unconditional
  delivery stack/chunk safety correction is retained; no separate capacity
  multiplier is included because the native vehicle-fit check and loader do
  not yet share a safe expanded-capacity transaction.
- **Known mixer note:** Mixing Station V1's skillcheck display can look like
  ingredients are missing; the reported issue is visual-only. Tested quantity
  and timing remained correct with no observed product loss. This update does
  not change that presentation.
- **Cash quick-move:** Cash can be shift-clicked between the player inventory
  and active general-purpose storage containers, including safes, closets, and
  racks. Employee lockers, Dead Drops, and Supplier Stashes retain their
  explicit native cash routes. Production machines, delivery storage, special
  placeables, and unknown storage remain excluded. Slot filters, cash acceptance,
  locks, and balance limits are respected; Sort behavior is unchanged.
- **Separate builds:** Use `BackpackInventoryQoL.dll` version `1.3.0` for MAIN
  or `BackpackInventoryQoL.Beta047.dll` version `1.3.0-beta.47` for BETA. Do not
  install both at once.

Drying Racks, Mixing Stations, and Plant Yield have been live-tested
successfully on MAIN and BETA. Packaging Stations and Delivery Capacity are
deferred from this release. This is a draft for later Nexus formatting;
publication is pending.
