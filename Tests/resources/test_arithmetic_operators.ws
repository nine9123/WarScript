# ──────────────────────────────────────────────────────
# test_arithmetic_operators.ws
# Covers: +, -, *, /, %, unary minus, string * n,
#         string - substring, array + array, mixed types
# ──────────────────────────────────────────────────────

# ── Basic integer arithmetic ──
assert 2 + 3 == 5
assert 10 - 4 == 6
assert 3 * 7 == 21
assert 15 / 3 == 5
assert 10 % 3 == 1

# ── Decimal arithmetic ──
assert 1.5 + 2.5 == 4
assert 10.0 - 3.5 == 6.5
assert 2.5 * 4 == 10
assert 15 / 4 == 3.75
assert 10.5 % 3 == 1.5

# ── Division produces decimals ──
assert 7 / 2 == 3.5

# ── Modulo edge cases ──
assert 0 % 5 == 0
assert 5 % 5 == 0
assert 6 % 5 == 1
assert 17 % 7 == 3

# ── Large numbers ──
assert 1000000 * 1000000 == 1000000000000
assert 999999 + 1 == 1000000

# ── Identity operations ──
assert 42 + 0 == 42
assert 42 - 0 == 42
assert 42 * 1 == 42
assert 42 / 1 == 42

# ── Unary minus on literals ──
assert -5 == 0 - 5
assert -0 == 0
assert -1 + 1 == 0

# ── Unary minus on variables ──
x = 10
assert -x == 0 - 10
y = -x
assert y == -10

# ── Unary minus in expressions ──
a = 5
b = 3
assert a + -b == 2
assert -a + b == -2
assert -a * -b == 15

# ── Double negation ──
v = 7
assert -(-v) == 7

# ── Negation of parenthesized expression ──
assert -(3 + 4) == -7
assert -(10 - 3) == -7

# ── String repetition ──
assert "ab" * 3 == "ababab"
assert "x" * 1 == "x"
assert "hi" * 0 == ""
assert 3 * "ab" == "ababab"

# ── String subtraction (Replace removes ALL occurrences) ──
assert "hello world" - "world" == "hello "
assert "test" - "xyz" == "test"

# ── String concatenation via + ──
assert "hello" + " " + "world" == "hello world"
assert "age: " + 25 == "age: 25"
assert 10 + " items" == "10 items"

# ── Array concatenation via + ──
assert {1, 2} + {3, 4} == {1, 2, 3, 4}
assert {} + {1} == {1}
assert {1} + {} == {1}
assert {} + {} == {}

# ── Chained arithmetic ──
assert 2 + 3 * 4 == 14
assert (2 + 3) * 4 == 20
assert 10 - 2 - 3 == 5
assert 10 / 2 / 5 == 1
