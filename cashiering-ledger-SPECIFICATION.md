# Cashiering Ledger — Build Specification

*A language-agnostic, step-by-step specification for building Project 1. This is
**not** a copy-and-paste tutorial. Each step states an objective, the requirements
the component must satisfy, the contract it must expose, and how you will verify it
— and leaves the implementation for you to design. Pseudocode appears only where an
algorithm is non-obvious, and it is a hint, not a target; deviate freely. Implement
in C#, Rust, or Python — the requirements do not change, only the idioms do.*

Companion document: `cashiering-ledger-ARCHITECTURE.md`. That document carries the
deep "why"; this one carries the "what to build and how to prove it." Read
architecture §3 (the domain), §6 (ports and adapters), and §11 (testing) first.

---

## How to use this document

Work one step at a time. For each step: read the requirements, design and implement
the component yourself, write the verification tests, get them green, commit, and
only then move on. Resist reading ahead for the shape of the answer — the value is
in deriving it.

Each step is structured as some subset of:

- **Objective** — what you are building and why it earns its place.
- **Requirements** — numbered, testable obligations (`R<step>.<n>`). "SHALL" is a
  hard obligation; "SHOULD" is a strong default you may override with reason.
- **Contract** — the operations the component must expose, in neutral notation.
  *What* goes in and comes out, and what errors are possible — never *how*.
- **Pseudocode** — a reference shape for a non-obvious algorithm. Optional reading;
  your structure may differ.
- **Design decisions** — genuine forks the spec leaves to you, with the trade-offs.
- **Verify** — the examples and properties your tests must assert. You write the
  generators and assertions in your language's testing tools.
- **Pitfalls** — the classic ways this goes wrong.
- **Language notes** — where the three target languages differ in idiom or tooling.
- **Done when** — the definition of done, and a suggested commit message.

If you are doing more than one language, build one end-to-end first, then port. The
port is itself an excellent exercise: it forces you to separate the requirements
(which are constant) from the idioms (which are not).

### Notation

```
Name { field : Type, ... }     a value type / record with named fields
T?                             an optional T (may be absent)
[T]  /  list of T              an ordered sequence of T
Map<K, V>                      an associative mapping
Result<T>                      an outcome: either Ok(value: T) or Fail(error: message)
fn name(params) -> ReturnType  an operation, followed by its contract/behaviour
<-                             assignment (in pseudocode)
```

`Result<T>` is a *concept*, not a prescribed type: some languages give it to you,
in others you build it, in others you choose another mechanism. See Step 1 and the
Language Toolbox appendix.

---

## Step 0 — Project structure and the dependency rule

**Objective.** Establish a layered structure that physically enforces the
dependency rule, so the domain cannot accidentally acquire a dependency on a
database, a file, or a clock. Get an empty version compiling and committed before
any logic exists.

**Requirements.**

- **R0.1** The codebase SHALL be separated into four layers: **Domain**,
  **Application**, **Infrastructure**, and an **Entry point** (CLI).
- **R0.2** **Domain** SHALL depend on nothing outside the language's standard
  library — no database driver, no file I/O, no CLI library, no clock.
- **R0.3** **Application** SHALL depend only on Domain. It SHALL define the
  abstract **ports** (interfaces) the system needs from the outside world.
- **R0.4** **Infrastructure** SHALL depend on Application and Domain, and SHALL
  contain the concrete **adapters** that implement the ports.
- **R0.5** The **Entry point** SHALL be the only place where concrete adapters are
  selected and wired to ports (the composition root). It may depend on all layers.
- **R0.6** The repository SHALL contain, from the first commit: a license file
  (`UNLICENSE`), a `.gitignore`, and a placeholder `README.md`.

**Design decisions.** How you express layers is yours: separate compilation units
(C# projects, Rust crates/modules, Python packages), or a single unit with a
linting rule that forbids upward imports. Whatever you pick, the test is mechanical:
*can the Domain layer import a database type?* If yes, the boundary is not real.

**Language notes.**
- *C#:* four projects in a solution (`Domain`, `Application`, `Infrastructure`,
  `Cli`) plus a test project; enforce R0.2–R0.5 with project references. Target
  **.NET 10 (LTS)**.
- *Rust:* a workspace with crates per layer (or one crate with modules and
  `pub(crate)` discipline); dependencies in each crate's `Cargo.toml` make the rule
  visible.
- *Python:* a package per layer under `src/`; enforce the import direction with a
  linter (e.g. an import-rules plugin) since Python won't stop you otherwise.

**Done when.** The empty skeleton builds and the layer boundaries are real.
Commit: `chore: scaffold layered structure and dependency rule`.

---

## Step 1 — Expected-failure type and Money

**Objective.** Lay the two foundations everything else rests on: a way to represent
*expected* failures as values, and a correct money type. Money is the
non-negotiable; getting it wrong is the fastest way to be dismissed in this field.

**Requirements.**

- **R1.1** Expected, recoverable failures (an entry that does not balance, an
  unknown account) SHALL be representable as ordinary return values, distinct from
  exceptional faults (a missing file, an unreachable database). The carrier is the
  `Result<T>` concept: success with a value, or failure with a reason.
- **R1.2** Monetary amounts SHALL NOT use binary floating-point (`float`/`double`).
  Represent them as an arbitrary-precision base-10 **decimal**, or as an integer
  count of **minor units** (e.g. cents).
- **R1.3** A monetary amount SHALL always carry its **currency**. An amount without
  a currency SHALL NOT be representable.
- **R1.4** Arithmetic between amounts of different currencies SHALL be rejected as
  an error, never silently coerced.
- **R1.5** Exactly **one** rounding policy SHALL exist — **banker's rounding
  (round-half-to-even)**, the finance default — applied at a single chokepoint
  (construction), not scattered through the code.
- **R1.6** The type SHALL convert **losslessly** in both directions between a money
  value and its integer minor-units representation (used later for storage).
- **R1.7** Two money values with equal amount and currency SHALL compare equal (so
  the balancing check in Step 3 can compare sums directly).

**Contract.**

```
Currency { code : string, minor_unit_digits : integer }

Money    { amount : decimal, currency : Currency }

fn Money.of(amount: decimal, c: Currency) -> Money
    rounds `amount` to c.minor_unit_digits using round-half-to-even, then constructs

fn Money.zero(c: Currency) -> Money
fn Money.add(a: Money, b: Money) -> Money        // precondition: a.currency == b.currency
fn Money.negate(a: Money) -> Money
fn Money.is_zero(a: Money) -> bool
fn Money.to_minor_units(a: Money) -> integer
fn Money.from_minor_units(units: integer, c: Currency) -> Money
```

**Design decisions.** (1) *Decimal vs. minor-units as the in-memory representation.*
A decimal type reads naturally and matches how amounts are written; integer
minor-units make exactness obvious and arithmetic trivially associative. Either
satisfies the requirements — pick one and be consistent. (2) *What to do on a
currency mismatch in `add`.* Is that an exceptional fault (throw/panic) or an
expected `Result` failure? Argue it either way, but be consistent with how you draw
the line in R1.1.

**Verify.**
- *Example:* `of(10.005, USD)` rounds to `10.00` (half-to-even); `of(10.015, USD)`
  rounds to `10.02`.
- *Example:* adding USD to EUR is rejected.
- *Property:* for any like-currency amounts, `add` is **commutative** and
  **associative**.
- *Property:* `from_minor_units(to_minor_units(m), m.currency) == m` for a wide
  range of generated amounts (round-trip).

**Pitfalls.** Constructing a decimal from a binary float before rounding
(re-introduces float error — parse from string or integer instead); applying
rounding in more than one place; letting a "neutral"/currencyless zero leak into
arithmetic with a real currency.

**Language notes.**
- *C#:* native `decimal` (128-bit, base-10) satisfies R1.2 directly; round with the
  banker's-rounding midpoint mode. Build a small `Result<T>`, or use a library.
- *Rust:* `rust_decimal`, or a newtype over `i64` minor units; `Result<T, E>` is
  built in — use it.
- *Python:* `decimal.Decimal` with an explicit context and rounding mode; for R1.1
  decide between a small result type, a result library, or exceptions-for-faults.

**Done when.** Money is correct and its properties pass. Commit:
`feat(domain): Money value type and expected-failure carrier`.

---

## Step 2 — Accounts, types, and the chart of accounts

**Objective.** Encode the relationship between an account's *type* and the *side its
positive balance sits on*, once, so every later calculation reads it rather than
re-deriving it. Seed the real cashiering accounts.

**Requirements.**

- **R2.1** An account SHALL have an identity, a human-readable name, a **type**
  (Asset, Liability, Equity, Revenue, Expense), and a currency.
- **R2.2** Each account type SHALL map to a **normal balance side** (Debit or
  Credit) by this fixed table: Asset → Debit, Expense → Debit, Liability → Credit,
  Equity → Credit, Revenue → Credit. This mapping SHALL be defined in exactly one
  place.
- **R2.3** The system SHALL provide a seeded **chart of accounts** for cashiering,
  using the real roles below — not abstract buckets.

The cashiering chart (architecture §5):

| Account | Type | Note |
|---|---|---|
| Cash in Bank | Asset | the firm's actual cash at its bank |
| Client Cash Payable | Liability | client free-credit balances — **the firm owes this back** |
| Settlement / Clearing | Asset | cash in flight to/from the clearing house (seam to Project 3) |
| Commission / Fee Revenue | Revenue | what the firm earns |
| Fees Receivable | Asset | fees accrued but not yet collected |
| Suspense | Liability | temporary home for unidentified items; auditors look here |

**Contract.**

```
NormalSide  = Debit | Credit
AccountType = Asset | Liability | Equity | Revenue | Expense

fn normal_side_of(t: AccountType) -> NormalSide

Account { id : AccountId, name : string, type : AccountType, currency : Currency }
    Account.normal_side derives from normal_side_of(type)
```

**Verify.** Assert the full type→side table (five cases). Trivial, but it pins the
table the entire balance computation depends on.

**Pitfalls.** Modelling Client Cash Payable as an Asset. From the broker's books it
is a **Liability** — the single most telling domain detail in this project. Getting
this wrong is a correctness bug *and* a credibility failure in an interview.

**Language notes.** All three languages have enums for `NormalSide`/`AccountType`.
Express the mapping as a single total function (a match/switch). In Rust this is a
natural exhaustive `match`; in C# a switch expression; in Python a `match` or a
lookup keyed by the enum.

**Done when.** The mapping is total and tested, and the chart is seeded. Commit:
`feat(domain): account types, normal sides, cashiering chart of accounts`.

---

## Step 3 — Postings, journal entries, and the balancing invariant

**Objective.** Build the heart of the domain: the atomic, *balanced* unit of record.
Construction must be the only way an entry comes into existence, and construction
must validate — an unbalanced entry must be impossible to hold.

**Requirements.**

- **R3.1** A **posting** SHALL be a single direction (Debit or Credit) applied to
  one account for a **positive** money amount.
- **R3.2** A **journal entry** SHALL carry: an identity; an **effective date** (the
  accounting date the event belongs to — an explicit input, never read from the
  wall clock); a **recorded-at** timestamp; a description; an **external reference**
  (the business key used for idempotency); and its postings.
- **R3.3** A journal entry SHALL contain **at least two** postings.
- **R3.4** All postings within an entry SHALL share **one** currency.
- **R3.5** Within an entry, the **sum of debit amounts SHALL equal the sum of
  credit amounts** (the per-entry balancing invariant).
- **R3.6** Construction SHALL validate R3.3–R3.5 and return a typed failure when any
  is violated. It SHALL NOT produce a half-valid entry. (Validate before you build;
  an invalid entry never exists.)

**Contract.**

```
Direction = Debit | Credit
Posting { account : AccountId, direction : Direction, amount : Money }

JournalEntry {
    id                 : EntryId,
    effective_date     : Date,
    recorded_at        : Timestamp,
    description        : string,
    external_reference : string,
    postings           : [Posting]      // length >= 2, one currency, balanced
}

fn JournalEntry.create(id, effective_date, recorded_at,
                       description, external_reference, postings) -> Result<JournalEntry>
```

**Pseudocode** (reference shape for `create`):

```
# pseudocode — your structure may differ
fn create(..., postings):
    if length(postings) < 2:
        return Fail("an entry needs at least two postings")
    currency <- postings[0].amount.currency
    if any p in postings where p.amount.currency != currency:
        return Fail("all postings must share one currency")
    debits  <- sum of p.amount for p in postings where p.direction == Debit   # via Money.add
    credits <- sum of p.amount for p in postings where p.direction == Credit
    if debits != credits:
        return Fail("entry does not balance: " + debits + " != " + credits)
    return Ok(JournalEntry { ... })
```

**Design decisions.** Should a *positive amount* (R3.1) be enforced by the `Money`
type, by `Posting` construction, or by entry validation? Each is defensible; decide
where the obligation lives and state it. Also: do you model Debit/Credit as an enum,
or push it into the type system so a posting *is* either a debit or a credit (a sum
type with data)? The latter is more work but eliminates a class of mistake.

**Verify.**
- *Examples (turn the worked entries from architecture §5 into acceptance tests):*
  the $1,000 deposit (Debit Cash in Bank / Credit Client Cash Payable) **constructs**;
  the $25 commission **constructs**; an entry with debits 100 and credits 90 **fails**
  with a balance error; a single-posting entry **fails**.
- *Property:* for any generated set of postings whose debit total equals its credit
  total, `create` succeeds; for any set where the totals differ, it fails. (This is
  your first property test — assert the invariant across thousands of inputs, not a
  handful.)

**Language notes.** Property testing: Hypothesis (Python), proptest or quickcheck
(Rust), CsCheck or FsCheck (C#). The generator produces lists of postings; the
"balanced" case is easiest to generate by constructing matched debit/credit pairs.

**Done when.** Balanced entries construct, imbalanced ones fail, and the property
holds. Commit: `feat(domain): postings and balanced journal entries with validation`.

---

## Step 4 — Balance computation

**Objective.** Reduce all of balance computation to one idea: the **signed effect**
of a posting on its account. Everything else — an account balance, the trial
balance — is a fold over this.

**Requirements.**

- **R4.1** Define the **signed effect** of a posting on its account: if the
  posting's direction matches the account's normal side, the effect is `+amount`;
  otherwise it is `−amount`.
- **R4.2** An account's **balance** SHALL be the sum of the signed effects of its
  postings. A positive balance reflects a normally-positioned balance for that
  account type.
- **R4.3** The signed-effect function SHALL be **pure** (no I/O, no clock) and live
  in the Domain layer, so it is unit-testable with no infrastructure.

**Pseudocode.**

```
# pseudocode
fn effect_on(posting, account_normal_side) -> Money:
    matching_direction <- Debit  if account_normal_side == Debit  else Credit
    if posting.direction == matching_direction:
        return posting.amount
    else:
        return negate(posting.amount)

# balance is then:  fold( effect_on(p, side) for p in account_postings ) starting from zero
```

**Verify.**
- *Examples:* a Debit to a debit-normal account yields `+amount`; the same Debit to
  a credit-normal account yields `−amount`.
- *Property (order independence):* fold a list of a single account's posting
  effects; shuffle the list; fold again — the totals are **equal**. (This follows
  from money addition being associative and commutative, which you proved in Step 1.
  It is a small property, but exactly the kind a back-office reviewer respects.)

**Pitfalls.** Trying to compute balances by special-casing each account type with
its own `if` ladder. There is one rule (R4.1); the account type enters only through
its normal side. Resist re-deriving the sign per type.

**Done when.** The effect function is pure and both properties pass. Commit:
`feat(domain): signed-effect balance computation`.

---

## Step 5 — The repository port and an in-memory adapter

**Objective.** Make the hexagonal boundary concrete. Define the **port** the
application needs for persistence, and a first **adapter** that lives entirely in
memory — so the whole invariant suite can run with no database anywhere.

**Requirements.**

- **R5.1** Define a **journal repository port** exposing exactly: append an entry;
  test whether an external reference already exists; fetch an entry by id; enumerate
  the postings for one account (optionally bounded by a date range); enumerate all
  postings (for the trial balance).
- **R5.2** The port SHALL expose **no update and no delete** operation. Append-only
  discipline is encoded in the *shape* of the port, so it cannot be violated by
  accident.
- **R5.3** Define a **clock port** exposing the current timestamp and current date,
  so the domain and services never read the wall clock directly and tests can pin
  time.
- **R5.4** Provide an **in-memory adapter** implementing the repository port. It is
  the development and test default.
- **R5.5** The in-memory adapter SHALL reject appending an entry whose external
  reference already exists (the storage-level idempotency guard).

**Contract.**

```
port JournalRepository:
    append(entry: JournalEntry)                       // rejects a duplicate external_reference
    exists_by_external_reference(ref: string) -> bool
    get_by_id(id: EntryId) -> JournalEntry?
    postings_for(account: AccountId, from: Date?, to: Date?) -> [(JournalEntry, Posting)]
    all_postings() -> [(JournalEntry, Posting)]

port Clock:
    now()   -> Timestamp
    today() -> Date
```

**Design decisions.** What does `postings_for` return — bare postings, or postings
paired with their entry? Statements and balances need the entry's effective date and
id, so returning the pair is usually less awkward. Decide and keep it consistent.
Also provide a **fixed clock** adapter for tests alongside the real one; pinned time
is what keeps runs deterministic (architecture §7).

**Verify.** Defer the heavy verification to Step 8's shared contract test, which
runs against *both* adapters. For now: append an entry and read it back; a duplicate
external reference is rejected; `postings_for` returns only the requested account's
postings, correctly filtered by date.

**Language notes.** "Port" = interface (C#), trait (Rust), Protocol or ABC (Python).
The in-memory adapter is a list plus a set of seen references plus an id index.

**Done when.** The ports are defined, the in-memory adapter passes its basic checks,
and a fixed clock exists. Commit: `feat: repository and clock ports, in-memory adapter`.

---

## Step 6 — Application services (the use cases)

**Objective.** Provide thin orchestration over the domain and the ports. The
services hold **no accounting rules of their own** — those are in the domain — only
sequencing, the idempotency check, and the reversal construction.

**Requirements.**

- **R6.1** `post(entry)` SHALL: reject a duplicate external reference; reject an
  entry that references an unknown account; then append. (The entry is already
  balanced by construction — the service does not re-check that.)
- **R6.2** `balance_of(account, as_of?)` SHALL return the account's balance as of a
  date (or current), by folding signed effects (Step 4).
- **R6.3** `statement(account, from, to)` SHALL return the in-range postings in
  effective-date order, each with a **running balance**.
- **R6.4** `trial_balance(as_of?)` SHALL fold every posting's signed amount (debit
  positive, credit negative) and confirm the net is **zero**. A non-zero result
  SHALL be treated as a loud failure, not a warning — it means corruption or a bug.
- **R6.5** `reverse(entry_id, reason)` SHALL: load the original; build a new entry
  with **every posting's direction flipped** and the same amounts; reference it back
  to the original (e.g. `reversal:{id}`); and post it. The original SHALL **never**
  be deleted.

**Pseudocode** (reference shape for `reverse`):

```
# pseudocode
fn reverse(entry_id, reason):
    original <- repo.get_by_id(entry_id)
    if original is absent: return Fail("entry not found")
    flipped <- [ Posting(p.account, opposite(p.direction), p.amount) for p in original.postings ]
    created <- JournalEntry.create(next_id(), clock.today(), clock.now(),
                                   "Reversal of " + entry_id + ": " + reason,
                                   "reversal:" + entry_id, flipped)
    if created is Fail: return created
    return post(created.value)
```

**Design decisions.** *Idempotency policy for a duplicate reference.* R6.1 has
`post` **reject** it, so an interactive caller is told. But the CSV importer (Step 7)
will instead **skip** duplicates, which is what makes re-running an import safe. Same
external-reference key, two sensible policies for two callers — decide this
consciously and document it. (This is a real design judgment, not an oversight.)

**Verify.** These are the properties that *sell* the project:
- **Reverse is an exact inverse:** post any balanced entry; record the balances of
  the accounts it touches; reverse it; assert every balance returns to its prior
  value.
- **Idempotency:** posting the same entry twice leaves the ledger identical to
  posting it once.
- **Trial balance is zero** after posting any generated set of balanced entries.

**Pitfalls.** Leaking accounting logic into the service (e.g. re-summing debits and
credits here) — the domain already guarantees balance; duplicating it invites drift.
Forgetting that `reverse` must itself go through normal validation and posting (it
produces a real, balanced entry; it is not a special back-door write).

**Done when.** The three properties pass against the in-memory adapter. Commit:
`feat(application): post, balance, statement, trial-balance, reverse`.

---

## Step 7 — File import and the first golden test

**Objective.** Turn a day of cashiering activity in a delimited file into journal
entries, and lock the end-to-end behaviour with a golden (snapshot) test.

**Requirements.**

- **R7.1** Provide an importer that reads activity rows from a delimited file and
  maps each to a journal entry via the same domain construction (Step 3). A simple
  deposit row, for instance, maps to *Debit Cash in Bank / Credit Client Cash
  Payable* for the amount.
- **R7.2** The importer SHALL collect parse and validation failures into a **report**
  and continue, rather than aborting on the first bad row.
- **R7.3** The importer SHALL **skip** any row whose external reference already
  exists, so re-running the same import is safe (idempotent re-import — contrast
  R6.1).
- **R7.4** Provide at least one realistic fixture file and its expected rendered
  output (trial balance and statements), checked into the repository.
- **R7.5** A **golden test** SHALL run the fixture through import → post → render
  and assert the output equals the checked-in expected file.

Example activity rows (a deposits file):

```
external_ref,effective_date,description,amount,client_account
DEP-0001,2026-01-02,client deposit,1000.00,2000
DEP-0002,2026-01-02,client deposit,250.00,2000
```

**Design decisions.** Hand-rolled splitting vs. a CSV library: a hand-rolled split
is fine for the MVP and keeps dependencies down; a library handles quoting and edge
cases you will eventually hit. Either is acceptable — note your choice and why. Also
decide your rendering format (the thing the golden file captures) and keep it stable;
churn in the renderer means churn in the golden file.

**Verify.** The golden test itself is the verification. Additionally: feed a file
with one malformed row and assert it lands in the failure report while the good rows
still post.

**Pitfalls.** Letting the golden test read fixtures from a path that does not exist
at test-run time — ensure fixtures are available to the test runner (copied to the
output/working directory as your toolchain requires). Embedding the wall-clock date
in rendered output (breaks determinism — use the fixed clock and explicit effective
dates).

**Language notes.** Delimited parsing: a CSV crate (Rust), `CsvHelper` (C#), or the
stdlib `csv` module (Python) — or hand-rolled in any of them. Golden comparison is
just a string equality assertion against file contents.

**Done when.** The golden test passes against committed fixtures and the failure
report works. Commit: `feat(infra): file importer and golden tests over sample activity`.

---

## Step 8 — Durable persistence (SQLite) and the contract test

**Objective.** Show the database swap costs **nothing** above the port: a second
adapter, durable this time, that passes the exact same behavioural tests as the
in-memory one.

**Requirements.**

- **R8.1** Provide a SQLite adapter implementing the journal repository port.
- **R8.2** The schema SHALL be two append-only tables: one for entries (with a
  **unique** constraint on the external reference — idempotency at the storage
  layer) and one for postings. Money SHALL be stored as **integer minor units**, not
  a float and not a decimal string.
- **R8.3** The adapter SHALL contain **no `UPDATE` and no `DELETE`** statements
  anywhere.
- **R8.4** Appending an entry and its postings SHALL be **atomic** — wrapped in one
  transaction — so a half-written entry can never land.
- **R8.5** Reads SHALL reconstruct money from stored minor units (Step 1, R1.6).
- **R8.6** A single **repository contract test** — one set of behavioural tests —
  SHALL run against **both** adapters and pass for both, proving they are
  interchangeable.

**Schema** (language-neutral; this is SQL, so it carries across):

```sql
CREATE TABLE IF NOT EXISTS journal_entry (
    id                 TEXT PRIMARY KEY,
    effective_date     TEXT NOT NULL,          -- ISO-8601 date
    recorded_at        TEXT NOT NULL,          -- ISO-8601 timestamp
    description        TEXT NOT NULL,
    external_reference TEXT NOT NULL UNIQUE     -- idempotency guaranteed by the DB
);

CREATE TABLE IF NOT EXISTS posting (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    entry_id     TEXT    NOT NULL REFERENCES journal_entry(id),
    account_id   TEXT    NOT NULL,
    direction    TEXT    NOT NULL CHECK (direction IN ('Debit','Credit')),
    amount_minor INTEGER NOT NULL,             -- exact, no floating point
    currency     TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_posting_account ON posting(account_id);
```

**Design decisions.** *Raw SQL vs. an ORM/query builder.* Raw SQL is recommended
here: it is lighter, keeps the append-only intent visible, and demonstrates you
understand the data model. An ORM slots in behind the same port if you want to show
that fluency for the job market — the rest of the system never knows, and that
substitutability is itself the point worth showing. *Contract-test structure:* a
shared, parameterised test that constructs "the repository under test" via a factory,
then runs every behaviour against whatever adapter the factory returns. For the
SQLite case in tests, back it with an in-memory database connection kept open for the
test's lifetime.

**Verify.** The contract test (R8.6) is the headline: append-then-read-back,
duplicate-reference rejection, date-range filtering — all green against both adapters.
Add one transactionality check: a deliberately failing second posting must leave *no*
trace of the entry (the transaction rolled back).

**Pitfalls.** Storing money as a float column (defeats the entire money discipline)
or as a decimal string (works, but minor-units integers are exact and sort/compare
correctly). Leaving the SQLite connection lifetime mismanaged in the in-memory test
(the database vanishes when the last connection closes).

**Language notes.** Drivers: `rusqlite` (Rust), `Microsoft.Data.Sqlite` (C#), the
stdlib `sqlite3` (Python). Parameterise every query — never string-concatenate
values into SQL.

**Done when.** Both adapters pass one shared contract test and the transaction check.
Commit: `feat(infra): SQLite append-only adapter + shared repository contract test`.

---

## Step 9 — The command-line interface

**Objective.** Provide the human interface and the composition root — the one place
concrete adapters are chosen and wired to ports.

**Requirements.**

- **R9.1** The entry point SHALL be the only place a concrete repository (in-memory
  or SQLite) and clock are selected and injected.
- **R9.2** Provide five commands matching the use cases: **post** (from a file or
  inline arguments), **balance** (with an optional as-of date), **statement** (with a
  date range), **trial-balance** (with an optional as-of date), and **reverse** (with
  a reason).
- **R9.3** Each command SHALL render the `Result` of its use case clearly: the
  success output, or the failure reason.

**Design decisions.** Manual argument parsing with a dispatch on the first argument
is a perfectly defensible MVP. A CLI framework buys you sub-command binding and
formatted tables for statements and the trial balance with little effort — worth it
for a portfolio piece you want to *look* finished. Your call.

**Verify.** A handful of end-to-end checks driving the commands and asserting on
rendered output (these can reuse the golden-test rendering). Keep them few; the heavy
correctness lives in the domain and application tests.

**Language notes.** CLI frameworks: `clap` (Rust), `System.CommandLine` or
`Spectre.Console.Cli` (C#), `argparse` (stdlib) or `click`/`typer` (Python).

**Done when.** The five commands work against a chosen adapter. Commit:
`feat(cli): post, balance, statement, trial-balance, reverse commands`.

---

## Step 10 — Observability and audit

**Objective.** Make "what happened and why" answerable after the fact — the
audit-mindedness the role demands, and cheap to add.

**Requirements.**

- **R10.1** The system SHALL emit a **structured log line** for every posting, every
  reversal, and every **rejected** entry **with its reason**.
- **R10.2** Each import or CLI run SHALL record **run metadata**: what triggered it,
  when, the row/entry counts, and a **content hash (SHA-256) of the input file**, so
  any run is reproducible and tamper-evident.

**Verify.** Assert that a rejected entry produces a log line carrying the rejection
reason, and that an import run records the input hash and counts. (These can be light
assertions over a captured log sink.)

**Pitfalls.** Logging free-text blobs instead of structured fields (you want to be
able to query "all rejections on date X"). Hashing the *parsed* data rather than the
*raw* input bytes (hash the bytes — that is what is reproducible).

**Language notes.** Structured logging: the `tracing` crate (Rust),
`Microsoft.Extensions.Logging` (C#), the stdlib `logging` with structured fields or
`structlog` (Python). SHA-256 is in every standard library.

**Done when.** Postings, reversals, and rejections are logged with reasons, and runs
carry an input hash. Commit: `feat: structured logging and run metadata with input hashing`.

---

## Step 11 — Consolidate the invariant suite

**Objective.** Step back and make the properties from Steps 3, 4, and 6 a single
coherent, green suite — your biggest differentiator.

**Requirements.**

- **R11.1** The following invariants SHALL each be covered by a property test:
  (a) an entry is accepted **iff** debits equal credits; (b) the trial balance is
  always zero for any set of balanced entries; (c) `balance_of` is an
  order-independent fold; (d) reverse is an exact inverse; (e) posting is idempotent
  on the external reference; (f) totals are conserved across the ledger.
- **R11.2** The golden test from Step 7 SHALL run against committed fixtures as part
  of the same suite.
- **R11.3** The README SHALL contain a short, prominent section naming these
  invariants and how they are tested.

**Verify.** The suite is the verification. Read each property back against its
requirement (R3.5, R4.2, R6.5, R6.1, conservation) and confirm nothing is asserted by
example alone where a property is achievable.

**Done when.** All invariant properties and the golden test pass together. Commit:
`test: consolidate property-based invariant suite`.

---

## Step 12 — README, CI, and ship

**Objective.** Make it presentable and reproducible, then **stop**.

**Requirements.**

- **R12.1** The `README.md` SHALL state: the problem in plain English; the cashiering
  domain and **why client cash is a liability**; how to build and run; what the
  sample data shows; the invariant suite (R11.3); and a short design-rationale
  section pointing at `ARCHITECTURE.md`.
- **R12.2** CI SHALL build and run the full test suite on every push.
- **R12.3** `UNLICENSE` SHALL be at the repository root. Tag a release (`v0.1.0`).

**Done when.** CI is green on a fresh checkout and the README stands on its own. This
project is finished — a clean, finished ledger you can walk an interviewer through
beats three impressive-sounding repos that do not run, and it is the foundation
Projects 2 and 3 build on. Commit: `docs: README and CI; v0.1.0`.

---

## Extensions (only after v0.1.0 ships)

In rough order of value to the portfolio, and re-stated here as further exercises
rather than requirements: **multi-currency** (an FX revaluation entry and
per-currency trial balances); **balance snapshots** folded forward for speed (name it
in the README first — it shows you know the lever before you pull it); a **period
close**; an **HTTP API** behind the same services; and the one that wires the
portfolio together — **postings that span cash and securities**, the seam through
which Project 3's settlement simulator will post into this ledger as its accounting
backend.

---

## Build-order recap

```
Step 0  structure + dependency rule
Step 1  expected-failure type + Money
Step 2  accounts + chart of accounts
Step 3  postings + balanced entries          ← first property test (R3.5)
Step 4  signed-effect balance computation    ← order-independence property (R4.2)
Step 5  repository + clock ports, in-memory adapter   ← hexagonal boundary
Step 6  application services + reverse        ← reverse-is-inverse, idempotency
Step 7  file import                           ← first golden test
Step 8  SQLite adapter                        ← repository contract test
Step 9  CLI                                   ← composition root
Step 10 observability + audit
Step 11 consolidate invariants
Step 12 README + CI + ship
```

Steps 1–6 are the whole correct core, exercisable from tests with no database, no
files, no CLI. If an evening is short, that core is where the real signal lives —
everything from Step 7 on is adapters around a domain that is already provably right.

---

## Appendix — Language Toolbox

Pointers to the idiomatic tool in each target language for each cross-cutting
concern. These are *where to look*, not implementations — the design is still yours.

| Concern | C# (.NET 10) | Rust (stable) | Python (3.11+) |
|---|---|---|---|
| Money / exact decimal | native `decimal` | `rust_decimal`, or `i64` minor-unit newtype | `decimal.Decimal` (explicit context) |
| Expected-failure carrier | build a `Result<T>`, or a library | built-in `Result<T, E>` / `Option<T>` | small result type, `returns` lib, or exceptions-for-faults |
| Immutable value object | `record` / `readonly record struct` | `struct` (immutable by default) + derives | `@dataclass(frozen=True)` / `NamedTuple` |
| Enum / sum type | `enum`; data-carrying via class hierarchy | `enum` (true sum type — the strong suit) | `enum.Enum`; data-carrying via classes + `match` |
| Port / interface | `interface` | `trait` | `Protocol` / ABC |
| Property testing | CsCheck or FsCheck | `proptest` or `quickcheck` | Hypothesis |
| Example/unit testing | xUnit (or NUnit / TUnit) | built-in `#[test]` / `cargo test` | pytest (or `unittest`) |
| SQLite driver | `Microsoft.Data.Sqlite` | `rusqlite` | stdlib `sqlite3` |
| CLI framework | `System.CommandLine` / `Spectre.Console.Cli` | `clap` | `argparse` (stdlib) / `click` / `typer` |
| Structured logging | `Microsoft.Extensions.Logging` | `tracing` | stdlib `logging` / `structlog` |
| Build / run | `dotnet` | `cargo` | `python -m` / `uv` |
| Formatter | `dotnet format` | `rustfmt` | `ruff format` / `black` |
| CI setup action | `actions/setup-dotnet` | `dtolnay/rust-toolchain` | `actions/setup-python` |

A note on the three as *learning* vehicles for this project specifically: **Rust**
rewards you most on Steps 1–4 and 6 — newtypes and sum types let you make illegal
states fail to compile, which is the correctness story this domain is built for.
**C#** is the fastest to a finished, job-aligned artifact and has the best native
money type. **Python** is the quickest to get a first end-to-end pass running and the
easiest place to experiment with the shape of the design before committing to it in a
stricter language — a legitimate first stop if you want to sketch before you build.
