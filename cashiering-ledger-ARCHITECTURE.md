# Cashiering Ledger — Architecture

_Project 1 of the financial back-office portfolio. This is the deep design
document for the repo; a condensed version belongs in the repo's
`ARCHITECTURE.md` alongside the master portfolio document's shared-foundations
section._

---

## 1. What this project is, and what it proves

A double-entry **cashiering ledger**: the accounting core that every brokerage
back office runs on. Money and securities move; every movement is recorded as a
balanced journal entry; nothing is ever edited or deleted; balances and the trial
balance are _derived_ from an immutable log.

It is built first because it is the smallest fully-correct core in the portfolio
and because Projects 2 (reconciliation) and 3 (settlement) lean on the same money
handling and, in the settlement case, can post directly into this ledger.

The signal to a hiring manager is specific and deliberate. Not "I can write a CRUD
app" — anyone can. The signal is: _this person understands double-entry accounting
cold, treats money correctly, builds for audit and immutability from the first
commit, and tests the invariants that actually matter in finance._ The framing is
brokerage cashiering — client cash, settlement, fees, suspense — because that is
the work, not generic bookkeeping.

---

## 2. Scope

|             | Included                                                                                                                                                                                                                                                                                  |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **MVP**     | Single currency. Post / validate / reverse. `balanceOf`, `statement`, `trialBalance`. CSV import with idempotent dedupe. In-memory and SQLite persistence behind one port. CLI. Property tests for the balancing and conservation invariants; golden tests over a day of sample activity. |
| **Stretch** | Multi-currency with FX revaluation. Balance snapshots for speed. Period close. HTTP API (ASP.NET minimal). Postings that span cash _and_ securities — the hook into Project 3.                                                                                                            |

Ship the MVP fully — README, tests, fixtures, CI, `UNLICENSE` — before touching
any stretch item. A finished ledger you can talk through in an interview beats a
half-built one with impressive-sounding TODOs.

---

## 3. The domain in plain English

Double-entry accounting rests on one idea: **every economic event touches at least
two accounts, and within the event the debits equal the credits.** Money is never
created or destroyed inside the books; it only moves between accounts.

Each account has a _type_ and, following from that type, a _normal balance side_:

| Type      | Normal side | A positive balance means                    | Increased by |
| --------- | ----------- | ------------------------------------------- | ------------ |
| Asset     | Debit       | the firm holds value (cash, receivables)    | Debit        |
| Expense   | Debit       | the firm has spent                          | Debit        |
| Liability | Credit      | the firm owes value (client cash, payables) | Credit       |
| Equity    | Credit      | residual owner value                        | Credit       |
| Revenue   | Credit      | the firm has earned                         | Credit       |

A **posting** hits one account with a **direction** (Debit or Credit) and a
**positive** amount. Whether that posting raises or lowers the account's balance
depends on whether its direction matches the account's normal side. Formally,
define the _signed effect_ of a posting on its account:

```
effect(posting) = +amount   if posting.direction == account.normalSide
                = −amount   otherwise
```

Then an account's balance is simply the sum of the effects of its postings, and a
positive number reflects a normally-positioned balance. This one function is the
whole of balance computation; everything else is bookkeeping around it.

Two invariants follow, and the system enforces both:

1. **Per-entry balance.** Within every journal entry, the sum of debit amounts
   equals the sum of credit amounts. An entry that does not balance is rejected
   _before_ it is ever persisted.
2. **System-wide trial balance.** Across the entire ledger, total debits equal
   total credits — equivalently, the sum of all signed posting amounts (debit
   positive, credit negative) is zero. Because every accepted entry balances, the
   ledger balances; the trial balance is therefore a _redundant_ check whose only
   job is to catch storage corruption or a bug that let an unbalanced entry slip
   through. In a financial system you want exactly that kind of redundant check.

---

## 4. Domain model

The domain layer is pure: no I/O, no database, no clock, no file reads. It is a set
of value objects and entities plus the two invariants above.

### Money

Monetary amounts are **never** `float`/`double`. Floating point silently loses
pennies; using it here is the single fastest way to be (correctly) dismissed by
anyone in this field. Money is modelled as a value object carrying an
**arbitrary-precision decimal amount together with its currency**. In the SQLite
adapter it is _stored_ as an integer count of minor units (cents) for exact,
deterministic persistence, and reconstituted to a decimal on read.

Three rules govern money and are enforced in one place each:

- An amount without a currency is a bug; `amount` and `currency` always travel
  together.
- Arithmetic across differing currencies is rejected, not silently coerced.
- Exactly one rounding policy exists — **banker's rounding (round-half-to-even)**,
  the finance default — and it is applied at one chokepoint, not sprinkled through
  the code.

### Accounts and the chart of accounts

An **Account** has an id, a human name, a type (the enum above), the normal side
derived from that type, and a currency. The set of accounts is the **chart of
accounts**; for cashiering it is seeded with the real roles, not abstract A/B/C
buckets (see §5).

### Postings and journal entries

A **Posting** is `(accountId, direction, money)` with a positive amount. A
**JournalEntry** is the atomic, balanced unit of record:

- a stable id,
- an **effective date** (the accounting date the event belongs to — an explicit
  input, never inferred from the wall clock inside the domain),
- a **recorded-at** timestamp (when it entered the system),
- a description,
- an **external reference** — the business key used for idempotency,
- two or more postings, all in the same currency, whose debits equal their credits.

Construction is the only place an entry comes into existence, and construction
_validates_: at least two postings, a single currency, debits equal to credits.
A failed validation returns a typed failure (see §10); it does not produce a
half-valid object. Illegal states are unrepresentable by construction.

---

## 5. Cashiering chart of accounts (the domain edge)

This is where eight years of post-trade experience shows. Model the real roles:

- **Cash in Bank** — _Asset._ The firm's actual cash at its bank.
- **Client Cash Payable** — _Liability._ Client free-credit balances. This is the
  detail that separates someone who has done the job from someone who has read a
  textbook: from the broker's books, **client cash is a liability** — the firm owes
  it back. One sub-account per client in practice; one omnibus account is fine for
  the MVP.
- **Settlement / Clearing Account** — _Asset (or clearing suspense)._ Cash in
  flight to or from the clearing house; the natural seam to Project 3.
- **Commission / Fee Revenue** — _Revenue._ What the firm earns.
- **Fees Receivable** — _Asset._ Fees accrued but not yet collected.
- **Suspense** — _Liability._ A deliberately temporary home for unidentified
  items, to be cleared promptly. Auditors look here.

Worked entries, to make the mechanics concrete (and to show a reviewer you can
think in journal entries, not just words):

**Client deposits \$1,000.**

| Account                         | Dir    | Amount | Effect                         |
| ------------------------------- | ------ | ------ | ------------------------------ |
| Cash in Bank (Asset)            | Debit  | 1,000  | +1,000 (firm's cash up)        |
| Client Cash Payable (Liability) | Credit | 1,000  | +1,000 (firm owes client more) |

**Charge a \$25 commission against the client's cash.**

| Account                         | Dir    | Amount | Effect                      |
| ------------------------------- | ------ | ------ | --------------------------- |
| Client Cash Payable (Liability) | Debit  | 25     | −25 (firm owes client less) |
| Commission Revenue (Revenue)    | Credit | 25     | +25 (firm earns)            |

**Disburse \$500 to the client's external bank.**

| Account                         | Dir    | Amount | Effect |
| ------------------------------- | ------ | ------ | ------ |
| Client Cash Payable (Liability) | Debit  | 500    | −500   |
| Cash in Bank (Asset)            | Credit | 500    | −500   |

**Unidentified \$200 wire arrives**, then is identified to a client:

| Step            | Debit            | Credit                  |
| --------------- | ---------------- | ----------------------- |
| On receipt      | Cash in Bank 200 | Suspense 200            |
| Once identified | Suspense 200     | Client Cash Payable 200 |

Every one of these balances. Cash sweeps (move client cash to/from a sweep
vehicle) and fee accruals (Debit _Fees Receivable_ / Credit _Fee Revenue_, later
collected against _Client Cash Payable_) follow the same shape.

---

## 6. Architecture: ports and adapters (hexagonal)

The design is hexagonal so the domain stays pure and everything external is a
swappable adapter. This is what makes the invariants testable without a database,
lets SQLite become Postgres without touching domain logic, and keeps the design
identical whether the implementation language is C# or Rust.

```
                ┌─────────────────────────────────────────┐
   CLI  ───────▶│             Application                  │
  (CSV) ───────▶│  use cases: Post, Balance, Statement,   │
  (HTTP)───────▶│  TrialBalance, Reverse                  │
                │                                          │
                │   defines PORTS (interfaces):            │
                │     IJournalRepository, IClock           │
                │                                          │
                │            ┌──────────────┐              │
                │            │    Domain    │  (pure)      │
                │            │  Money,      │              │
                │            │  Account,    │              │
                │            │  Posting,    │              │
                │            │  JournalEntry│              │
                │            │  invariants  │              │
                │            └──────────────┘              │
                └─────────────────────────────────────────┘
                         ▲                  ▲
        ADAPTERS implement the ports        │
   ┌──────────────────┐   ┌──────────────────┐   ┌───────────┐
   │ InMemoryJournal  │   │ SqliteJournal    │   │ SystemClock│
   │ Repository       │   │ Repository       │   │            │
   └──────────────────┘   └──────────────────┘   └───────────┘
```

**The dependency rule, strictly:** Domain depends on nothing. Application depends
only on Domain and defines the port interfaces. Infrastructure depends on
Application and Domain (it _implements_ the ports). The CLI is the composition
root — it depends on everything and is the only place concrete adapters are wired
to ports. Dependencies always point inward, toward the domain.

**Ports** (interfaces owned by the Application layer):

- `IJournalRepository` — `Append(entry)`, `ExistsByExternalReference(ref)`,
  `GetById(id)`, `PostingsFor(accountId, dateRange?)`, and an enumeration of all
  postings for the trial balance. Note what is **absent**: there is no `Update` and
  no `Delete`. The append-only discipline is encoded in the shape of the port, so
  it cannot be violated by accident.
- `IClock` — `Now()` / `Today()`, so the domain never reaches for `DateTime.Now`
  directly and tests can pin time.

---

## 7. Cross-cutting principles

These apply everywhere and are most of what separates a toy from something a
back-office engineer recognises as real.

**Append-only and event-sourced where it counts.** The journal is the immutable
event log. You never edit or delete a posting; a mistake is corrected by posting a
_reversing_ entry that references the original (§9). All state — balances,
statements, the trial balance — is _derived_ from the log. The audit trail is a
feature regulators and auditors live in, not an afterthought.

**Idempotency.** Re-ingesting the same file must not double-count. Every entry
carries an external/business reference; appending a duplicate reference is a
no-op (or a typed rejection), enforced both by the application service and by a
unique constraint in the SQLite adapter. CSV import dedupes on the same key.

**Determinism.** Given the same inputs, a run produces the same output: no hidden
randomness, no domain logic that branches on wall-clock time. Effective dates are
explicit inputs. Determinism is what makes runs reproducible for auditors and
trivial to test.

**Money is never a float** (restated because it is non-negotiable). One rounding
policy, applied in one place; currency carried with every amount; integer minor
units at the storage boundary.

---

## 8. Persistence

Two append-only tables behind the repository port:

- `journal_entry` — id, effective date, recorded-at, description, and a
  **unique** external reference (the idempotency guarantee at the storage layer).
- `posting` — entry id (foreign key), account id, direction (constrained to
  `Debit`/`Credit`), amount stored as **integer minor units**, and currency.

Balances are **derived** by folding postings. For the MVP, compute on read — it is
simpler, always correct, and fast enough for portfolio-scale data. Balance
_snapshots_ (periodic materialised balances you fold forward from) are a stretch
optimisation; mention them in the README as the obvious next step and you have
shown you know where the performance lever is without prematurely pulling it.

Two adapters implement the one port:

- **`InMemoryJournalRepository`** — lists and dictionaries. This is the
  development and test default; the entire invariant suite runs against it with no
  database in sight. Building this first is what makes the hexagonal payoff
  tangible.
- **`SqliteJournalRepository`** — `Microsoft.Data.Sqlite`, one transaction per
  appended entry, no `UPDATE` or `DELETE` statements anywhere in the file. Raw SQL
  is recommended here over an ORM: it is lighter, keeps the append-only intent
  visible, and demonstrates you understand the actual data model. (If you prefer to
  show EF Core fluency for the .NET job market, it slots in at exactly this layer
  behind the same port — the rest of the system never knows.)

A **repository contract test** — one shared set of tests parameterised over both
adapters — proves they behave identically. It is a small amount of code that reads
as real engineering maturity.

A Postgres adapter is an optional stretch that demonstrates the swap costs nothing
above the port.

---

## 9. Application layer (use cases)

Thin services orchestrate the domain and the ports. They hold no business rules of
their own beyond sequencing; the rules live in the domain.

- **`Post(entry)`** — check the external reference is unused (idempotency),
  confirm the referenced accounts exist, rely on the entry already being balanced
  by construction, then `Append`.
- **`BalanceOf(account, asOf?)`** — current or as-of-date balance, by folding
  effects.
- **`Statement(account, dateRange)`** — the postings in range with a running
  balance.
- **`TrialBalance(asOf)`** — fold every posting; assert total debits equal total
  credits. The system-wide health check.
- **`Reverse(entryId, reason)`** — load the original, build a new entry with every
  posting's direction flipped and the same amounts, reference it back to the
  original (`reversal:{id}`), and append it. The original is **never** deleted; both
  remain in the log and the net effect on every touched account is zero.

---

## 10. Error handling

Distinguish _expected domain failures_ from _exceptional faults_.

Expected failures — an entry that does not balance, an unknown account, a
duplicate external reference, a reversal of a non-existent entry — are part of
normal operation and are returned as a typed **`Result`** (success-or-failure with
a reason), not thrown. This keeps the failure paths in the type system where the
compiler and the reader can see them, reads cleanly, and pairs naturally with the
sum-type / exhaustive-match instincts you are sharpening in Rust.

Genuinely exceptional faults — the database is unreachable, a file is corrupt — are
exceptions, because there is nothing sensible the caller can do inline.

The cardinal rule, stated once: **validate before you persist.** An invalid entry
never reaches storage.

---

## 11. Testing strategy (the biggest differentiator)

Ordinary unit tests cover the obvious cases. The portfolio signal comes from the
two layers above them.

**Property-based tests** assert invariants over thousands of generated inputs
rather than a handful of hand-picked examples. The properties worth owning here:

- An entry is accepted **iff** its debits equal its credits — imbalanced entries
  are always rejected, balanced ones always accepted.
- For _any_ set of balanced entries, the trial balance is zero.
- `balanceOf` is the fold of an account's posting effects, and that fold is
  **order-independent** (money addition is associative and commutative) — shuffling
  the entries never changes a balance.
- **Reverse is an exact inverse:** posting `e` then reversing it returns every
  affected account to its prior balance.
- **Idempotency:** posting the same entry (same external reference) twice has the
  same effect as posting it once.
- **Conservation:** total debits and total credits across the ledger move in
  lockstep — the seam that becomes "cash and securities are conserved" in Project 3.

**Golden / snapshot tests** run a realistic `fixtures/` file — a full day of
cashiering activity — through the pipeline and compare the statement and
trial-balance output to a checked-in expected file. These catch unintended changes
in behaviour and double as living documentation of what the system does.

---

## 12. Observability and audit

In finance, "what happened and why" must be answerable after the fact. Structured
logging records every meaningful event: each posting, each reversal, each
_rejected_ entry with its reason. Each run records metadata — what triggered it,
when, the input counts, and a **SHA-256 hash of the input file** so a run is
reproducible and tamper-evident. This is cheap to add and it speaks directly to the
audit-mindedness the role demands.

---

## 13. Solution layout (.NET)

Target **.NET 10 (LTS)** — current through November 2028, the right default for a
new build.

```
CashieringLedger.sln
├── src/
│   ├── Ledger.Domain/          // pure: Money, Account, Posting, JournalEntry, invariants
│   ├── Ledger.Application/     // use cases + port interfaces (IJournalRepository, IClock)
│   ├── Ledger.Infrastructure/  // adapters: InMemory + Sqlite repos, CSV importer, clock, logging
│   └── Ledger.Cli/             // composition root + command parsing
├── tests/
│   └── Ledger.Tests/           // unit + property (CsCheck) + golden, incl. the repo contract test
├── fixtures/                   // sample activity CSVs and expected golden outputs
├── ARCHITECTURE.md             // the condensed version of this document
├── README.md
├── UNLICENSE
└── .github/workflows/ci.yml    // build + test on every push
```

The project references encode the dependency rule: Application → Domain;
Infrastructure → Application, Domain; Cli → everything; Tests → what they test.

---

## 14. Language

**C#** is the default and the recommendation for this project: native `decimal` is
a genuine advantage for money, it is the fastest path to a finished, job-aligned
portfolio piece, and .NET dominates the back-office shops you are targeting — with
a lineage (Borland / Anders Hejlsberg) that runs straight back through your Delphi
years.

**Rust** is a legitimate alternative whose payoff is correctness-by-construction —
newtypes for `Money`/`AccountId` and enums for direction and account type make
illegal states fail to compile, and it advances your Rustlings work. The honest
trade-off: slower to a finished artifact early on. The portfolio plan reserves Rust
for Project 3 (the settlement state machine, where exhaustive matching shines) and
keeps the ledger in C#. That split is yours to override, but it is the pragmatic
one if a paycheck is the near-term constraint.

---

## 15. How this composes with Projects 2 and 3

The ledger is deliberately the bottom layer. Project 3 (settlement) can post its
cash and securities movements **into** this ledger as its accounting backend — the
`Settlement / Clearing Account` and a future securities-leg posting are the seams.
Project 2 (reconciliation) stands alone but shares this project's money handling
and domain vocabulary. Building the ledger first, correctly, is what makes the
other two cheaper and what lets you tell a _systems_ story in an interview rather
than a _scripts_ one.

---

## 16. Glossary

- **Posting** — a single debit or credit to one account.
- **Journal entry** — an atomic, balanced set of two or more postings.
- **Debit / Credit** — the two posting directions; their effect on a balance
  depends on the account's normal side.
- **Normal side** — the side (debit or credit) on which an account's positive
  balance naturally sits, determined by its type.
- **Trial balance** — the system-wide check that total debits equal total credits.
- **Chart of accounts** — the full set of accounts and their types.
- **Reversing entry** — a new entry that flips an earlier one to correct a mistake
  without deleting anything.
- **Free credit balance / client cash payable** — client cash held by the firm; a
  _liability_ on the firm's books.
- **Suspense account** — a temporary holding account for unidentified items.
- **Settlement / clearing account** — cash in flight to or from the clearing
  house; the seam to the settlement simulator.
- **Accrual** — recognising a fee earned before the cash is collected.

---

## 17. Repo conventions

`README.md` states the problem, the domain in plain English, how to run it, what
the sample data is, and a short design-rationale section. `ARCHITECTURE.md` carries
the condensed form of this document. `fixtures/` holds realistic input and expected
golden output. `UNLICENSE` sits at the root — public-domain dedication, consistent
with the rest of your work. CI builds and runs the full test suite on every push.
