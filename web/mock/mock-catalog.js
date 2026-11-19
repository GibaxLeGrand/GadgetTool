// Made-up gadgets for the mockup. No name here comes from the game.
// If extracted/wiki-catalog.js exists locally (never committed), the mockup uses it instead.
(function () {
  const g = (id, name, slotCost, category, superEffect, description) =>
    ({ id: "mock." + id, name, slotCost, category, superEffect, description });

  window.GT_MOCK_CATALOG = {
    source: "Made-up gadgets (mockup)",
    gadgets: [
      g("ring_magnet", "Ring Magnet", 1, "Rings", true, "Pick up rings from further away."),
      g("ring_saver", "Ring Saver", 1, "Rings", true, "Lose fewer rings when hit."),
      g("lap_ring_bonus", "Lap Ring Bonus", 1, "Rings", true, "Get rings at the end of each lap."),
      g("start_ring_bonus", "Start Ring Bonus", 1, "Rings", true, "Get rings after a good start."),
      g("ring_cap_180", "Ring Cap 180", 2, "Rings", false, "Hold up to 180 rings."),
      g("ring_leech", "Ring Leech", 2, "Rings", false, "Take rings when bumping others."),
      g("ring_fortune_kit", "Ring Fortune Kit", 3, "Rings", false, "A kit for collecting many rings."),
      g("item_luck", "Item Luck", 1, "Items", true, "Better items a bit more often."),
      g("item_hold", "Item Hold+", 1, "Items", true, "Hold one more item."),
      g("item_swap", "Item Swap", 2, "Items", false, "Swap your item once per lap."),
      g("shield_start", "Shield Start", 2, "Items", false, "Start the race with a shield."),
      g("double_item_box", "Double Item Box", 2, "Items", false, "Item boxes sometimes give two items."),
      g("item_rush_kit", "Item Rush Kit", 3, "Items", false, "A kit for using many items."),
      g("quick_start", "Quick Start", 1, "Boost", true, "Stronger start boost."),
      g("landing_boost", "Landing Boost", 1, "Boost", true, "Boost when landing a trick."),
      g("bump_boost", "Bump Boost", 1, "Boost", true, "Small boost after a bump."),
      g("draft_boost", "Draft Boost", 1, "Boost", true, "Stronger slipstream boost."),
      g("long_boost", "Long Boost", 2, "Boost", false, "Boosts last longer."),
      g("boost_chain_kit", "Boost Chain Kit", 3, "Boost", false, "A kit for chaining boosts."),
      g("fast_charge_1", "Fast Charge Lv1", 1, "Drift", true, "Drift charge fills a bit faster."),
      g("fast_charge_2", "Fast Charge Lv2", 2, "Drift", false, "Drift charge fills faster."),
      g("tight_drift", "Tight Drift", 1, "Drift", true, "Sharper drifts."),
      g("charge_keeper", "Charge Keeper", 2, "Drift", false, "Keep part of the charge after a hit."),
      g("drift_master_kit", "Drift Master Kit", 3, "Drift", false, "A kit for drifting."),
      g("speed_5", "Speed +5", 1, "Stats", true, "Top speed +5."),
      g("accel_5", "Accel +5", 1, "Stats", true, "Acceleration +5."),
      g("handling_5", "Handling +5", 1, "Stats", true, "Handling +5."),
      g("power_5", "Power +5", 1, "Stats", true, "Power +5."),
      g("all_stats_4", "All Stats +4", 2, "Stats", false, "Every stat +4."),
      g("top_speed_kit", "Top Speed Kit", 3, "Stats", false, "A kit for top speed."),
      g("comeback_assist", "Comeback Assist", 2, "Support", false, "Better items when far behind."),
      g("spin_guard", "Spin Guard", 1, "Support", true, "Recover faster from spins."),
      g("trap_guard", "Trap Guard", 1, "Support", true, "Traps slow you down less."),
      g("rival_radar", "Rival Radar", 2, "Support", false, "See your rival's item."),
    ],
  };
})();
