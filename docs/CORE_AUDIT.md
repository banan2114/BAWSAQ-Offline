# Core and GTA Integration Audit

Audit date: 2026-11-26  
Audited revision: `d5ec65e58d7f4c02bf102281e6799872240c0e09`

This document records the current implementation and the recommended order of work. It is intentionally an audit/plan, not a claim that the items below have already been fixed.

## Current architecture

- `BawsaqOffline.Core` contains the company catalog, market engine, market events, stock models, market clock, portfolio and JSON persistence.
- `BawsaqOffline.GTA` contains the ScriptHookVDotNet entry point, the BAWSAQ menu, and the bridge to GTA player money.
- The GTA project references Core through a project reference. Its ScriptHookVDotNet3 assembly path is supplied by ignored `Local.props`, with `Local.props.example` committed as a template.
- Both projects target .NET Framework 4.8. The solution includes x64 configurations.

## Confirmed implementation

- The catalog currently defines 14 companies: 4 assigned to BAWSAQ and 10 to LCN.
- The GTA menu filters the catalog to `StockExchange.BAWSAQ`.
- The market engine updates prices using per-stock drift, volatility, and active-event drift modifiers.
- Market events can apply an immediate price impact and then modify drift for a configured number of market ticks.
- `MarketEventFactory` contains presets for Lester assassination missions.
- Portfolio holdings, stock prices/history, market clock state, and active events are serialized to JSON.
- The GTA integration charges/pays through `Game.Player.Money`; holdings are stored in the mod's own portfolio save.

## Findings and risks

### P1 — No automated Core tests

There is no test project in the solution. Market-clock conversion, deterministic market updates, trading invariants, event lifetimes, and save/load round trips currently have no repeatable automated coverage.

**Next action:** add a test project for Core before changing market behavior. Keep tests independent of ScriptHookVDotNet and GTA.

### P1 — Lester event presets are not wired to GTA mission events

The event factory exists, but `BawsaqScript` does not currently call it. The mission-specific market effects therefore cannot be triggered by completing the corresponding in-game missions through the current integration.

**Next action:** research the reliable ScriptHookVDotNet/GTA V signals for mission completion, then add a small event adapter. Avoid guessing mission state from unrelated UI text or player coordinates.

### P2 — BAWSAQ catalog is small

Only BET, FRT, FAC, and VAP are currently assigned to BAWSAQ. This may be intentional for the current milestone, but it is not a broad recreation of the in-game exchange.

**Next action:** establish the intended company list and source prices/identities before adding entries. Keep LCN and BAWSAQ membership explicit in the catalog.

### P2 — Trading quantity is fixed to one share in the UI

Both buy and sell handlers use a hard-coded quantity of 1. Core portfolio methods already accept arbitrary positive quantities.

**Next action:** add quantity selection and a preview of total cost/payout, with affordability and overflow checks performed before mutating the portfolio or GTA cash.

### P2 — Market time is wall-clock driven

The market clock maps elapsed real time to GTA's approximate 48-minute day. The GTA script calls it using `DateTime.Now`, and the load path catches up from the last saved wall-clock timestamp.

**Risk:** market progression may include time spent with the game paused or otherwise not ticking. Confirm the desired product behavior (offline progression vs. only active gameplay progression) before changing the clock source.

### P2 — Save/load needs failure-path coverage

Saving writes a temporary JSON file and replaces the previous save with a backup. Loading restores stock prices, histories, holdings, clock state, and events. This is a good baseline, but corrupt JSON, interrupted writes, missing backup recovery, and partially invalid records are not covered by tests.

**Next action:** add tests for valid round trips and malformed/partial saves. Then decide whether automatic recovery from `.bak` should be implemented.

## Recommended implementation order

1. Add Core tests (clock, portfolio, deterministic market tick, event lifecycle, save/load).
2. Decide and document the intended time semantics.
3. Implement variable-quantity trading in the GTA menu.
4. Research and wire Lester mission completion events.
5. Expand the BAWSAQ company catalog from a verified list.
6. Add recovery and diagnostics for failed/corrupt saves.

## Manual validation checklist

- Build the full solution in Visual Studio using Release|x64.
- Confirm the GTA project resolves the local ScriptHookVDotNet3 reference.
- Launch Story Mode and verify the initialization subtitle appears.
- Open and close the BAWSAQ menu repeatedly.
- Buy and sell a share and verify GTA cash and portfolio holdings remain consistent.
- Restart the game and verify stock prices and holdings are restored.
- Leave the game for a known interval and verify market catch-up matches the chosen time semantics.
- Trigger any implemented mission event and verify its target stock changes only once.
