Feature: ICT killzone classification at New York session boundaries
  The killzone clock classifies a candle's UTC open time into an ICT killzone in New York time
  (DST-aware, plan §2.5.5 / §4.8) — identical whatever zone the host runs in, via the IANA id
  America/New_York (never the Windows "Eastern Standard Time").

  Scenario Outline: An FX candle is classified by its New York wall-clock time
    Given an FX candle opening at "<ny_time>" New York time on a summer trading day
    When the killzone for that candle is evaluated
    Then the killzone should be classified as "<killzone>"

    Examples:
      | ny_time | killzone    |
      | 01:59   | None        |
      | 02:00   | LondonOpen  |
      | 04:59   | LondonOpen  |
      | 05:00   | None        |
      | 06:59   | None        |
      | 07:00   | NewYorkOpen |
      | 09:59   | NewYorkOpen |
      | 10:00   | LondonClose |
      | 12:30   | None        |
      | 20:00   | Asian       |
