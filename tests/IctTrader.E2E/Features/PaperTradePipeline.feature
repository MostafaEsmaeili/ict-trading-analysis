Feature: ICT setup confirmation drives the paper-trade pipeline end-to-end
  As the defensive, paper-trading-only ICT system
  I want a confirmed advisory setup to flow through the REAL Host
  So that it opens a paper trade, is managed candle-by-candle, closes, and updates performance and alerts
  Without ever touching a live-order path (the NON-NEGOTIABLE guardrail, plan §6.3)

  Background:
    Given a clean trading database
    And the real Host is booted over Testcontainers Postgres
    And the symbol "EURUSD" is being analysed
    And the market clock is anchored to New York time

  Scenario: A valid bullish London setup is paper-traded to its target and updates performance
    Given a confirmed bullish London-killzone setup from the Asian-sweep displacement model
    When the setup is confirmed on the bus
    Then a paper trade should be opened for "EURUSD"
    And the open trade should be advisory only with no live-order path
    When a candle trades up through the draw on liquidity
    Then the paper trade should close with outcome "TargetHit"
    And the performance summary should show a win rate of 100 percent over 1 trade
    And an advisory alert should record the confirmed setup
    And an advisory alert should record the closed trade

  Scenario: A stop-out candle books the loss through the same pipeline
    Given a confirmed bullish London-killzone setup from the Asian-sweep displacement model
    When the setup is confirmed on the bus
    Then a paper trade should be opened for "EURUSD"
    When a candle wicks below the protective stop
    Then the paper trade should close with outcome "StopHit"
    And the closed trade should realise minus one R
    And the performance summary should show a win rate of 0 percent over 1 trade
