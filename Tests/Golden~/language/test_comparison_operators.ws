# ──────────────────────────────────────────────────────
# test_comparison_operators.ws
# Covers: ==, !=, <, <=, >, >=, null comparisons,
#         cross-type comparisons, string comparisons
# ──────────────────────────────────────────────────────

# ── Numeric equality ──
assert 5 == 5
assert 0 == 0
assert -3 == -3
assert 3.14 == 3.14

# ── Numeric inequality ──
assert 5 != 3
assert 0 != 1
assert -1 != 1
assert 3.14 != 3.15

# ── Greater than ──
assert 10 > 5
assert 1 > 0
assert 0 > -1

# ── Greater than or equal ──
assert 10 >= 5
assert 5 >= 5
assert 0 >= -1

# ── Less than ──
assert 5 < 10
assert 0 < 1
assert -1 < 0

# ── Less than or equal ──
assert 5 <= 10
assert 5 <= 5
assert -1 <= 0

# ── Decimal comparisons ──
assert 3.14 > 3.13
assert 3.14 < 3.15
assert 3.14 >= 3.14
assert 3.14 <= 3.14

# ── String equality ──
assert "hello" == "hello"
assert "hello" != "world"
assert "" == ""
assert "a" != "b"
assert "abc" != "ABC"

# ── String comparison (lexicographic) ──
assert "b" > "a"
assert "a" < "b"
assert "abc" < "abd"
assert "z" > "a"
assert "apple" < "banana"

# ── Boolean equality ──
assert true == true
assert false == false
assert true != false
assert false != true

# ── Null comparisons ──
assert null == null
assert null != 5
assert null != "hello"
assert null != true
assert null != false
assert 5 != null
assert "hello" != null

# ── Chained comparisons in conditions ──
x = 5
assert x >= 0 and x <= 10
assert x > 0 and x < 10

# ── Negated comparisons (no parens around and/or) ──
x_in_range = x < 0 or x > 10
assert !x_in_range

# ── Comparison with expressions ──
assert 2 + 3 == 5
assert 10 - 3 == 7
assert 2 * 3 == 6
assert 10 / 2 == 5
assert 2 + 3 > 4
assert 2 + 3 < 6
assert 2 + 3 >= 5
assert 2 + 3 <= 5

# ── Comparison with variables ──
a = 10
b = 20
assert a < b
assert b > a
assert a != b
assert a == 10
assert b == 20
assert a + b == 30
