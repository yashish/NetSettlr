# NetSettlr
A high-throughput, deterministic clearing and settlement engine modeled after traditional automated clearing house (ACH) and deferred net settlement (DNS) systems.

Unlike Real-Time Payments (RTP) that process transactions individually, this platform aggregates, validates, and nets high-volume payment instructions over defined window intervals to minimize systemic liquidity requirements and primarily serves financial institutions, unlike RTP payment processors that can serve retail small money transactions.

Note that RTP in a Clearing and Settlement business still does not serve individual retail customers with their credit/debit card or Zelle/Paypal transactions but small businesses and customers expecting real time payments and liquidity. RTP in this sense may also be made in mini-batches and doesn't have to follow a strictly synchronous flow for every single transaction individually.


