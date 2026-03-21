# ──────────────────────────────────────────────────────
# test_operator_precedence.ws
# Covers: PEMDAS, logical vs comparison vs arithmetic,
#         parentheses override, mixed precedence chains,
#         unary vs binary, assignment precedence
# ──────────────────────────────────────────────────────

# ── Multiplication before addition ──
assert 2 + 3 * 4 == 14
assert 3 * 4 + 2 == 14
assert 10 - 2 * 3 == 4
assert 2 * 3 - 10 == -4

# ── Division before addition ──
assert 10 + 6 / 3 == 12
assert 6 / 3 + 10 == 12
assert 20 - 8 / 2 == 16

# ── Modulo same precedence as multiply/divide ──
assert 10 + 7 % 3 == 11
assert 7 % 3 + 10 == 11
assert 10 - 7 % 3 == 9

# ── Parentheses override (only with prec > 3 inside) ──
assert (2 + 3) * 4 == 20
assert 2 * (3 + 4) == 14
assert (10 - 2) * (3 + 1) == 32
assert (2 + 3) * (4 + 5) == 45

# ── Nested parentheses ──
assert ((2 + 3) * 4) + 1 == 21
assert 2 * ((3 + 4) * 5) == 70
assert (1 + (2 + (3 + 4))) == 10

# ── Comparison after arithmetic ──
assert 2 + 3 == 5
assert 2 + 3 > 4
assert 2 + 3 < 6
assert 2 + 3 >= 5
assert 2 + 3 <= 5
assert 2 * 3 == 6
assert 10 / 2 > 4
assert 10 % 3 == 1

# ── Comparison with expressions on both sides ──
assert 2 + 3 == 1 + 4
assert 10 - 3 > 2 + 3
assert 3 * 2 >= 5 + 1
assert 4 * 4 != 3 * 3

# ── Logical after comparison ──
assert 5 > 3 and 10 > 7
assert 5 > 3 or 10 < 7
cmp = 5 > 3 and 10 < 7
assert !cmp
assert 5 < 3 or 10 > 7

# ── And before or ──
assert true or false and false
assert false and true or true
assert false or true and true

# ── Complex mixed precedence ──
# 2 + 3 * 4 > 10 and 5 < 10
# => 14 > 10 and 5 < 10 => true and true => true
assert 2 + 3 * 4 > 10 and 5 < 10

# ── Not has high precedence ──
assert !false
assert !false and true

# ── Not with comparison (ok inside parens: prec 4 > 3) ──
# ── Not with comparison (Bug 1 fix: !(expr) now works) ──
assert !(5 < 3)
assert !(5 == 6)
assert !(10 <= 3)

# ── Unary minus precedence ──
x = 5
assert -x == -5
assert -x + 10 == 5
assert 10 + -x == 5
assert -x * 2 == -10
assert 2 * -x == -10

# ── Parenthesized negation ──
assert -(3 + 4) == -7
assert -(2 * 5) == -10
assert -(-5) == 5

# ── Assignment has lowest precedence ──
a = 2 + 3
assert a == 5

b = 2 + 3 * 4
assert b == 14

c = 5 > 3
assert c == true

d = 5 > 3 and 10 > 7
assert d == true

# ── Array append has low precedence ──
arr = {}
arr << 2 + 3
assert arr{0} == 5

arr << 2 * 3
assert arr{1} == 6

# ── Chained arithmetic left-to-right ──
assert 10 - 3 - 2 == 5
assert 100 / 10 / 2 == 5
assert 2 * 3 * 4 == 24
assert 1 + 2 + 3 + 4 == 10

# ── String + has addition precedence ──
assert "val: " + 2 + 3 == "val: 23"
assert "val: " + (2 + 3) == "val: 5"

# ── Complex real-world expressions ──
hp = 100
defense = 20
raw_damage = 50
net = hp - (raw_damage - defense)
assert net == 70

# ── Comparison chains with parentheses (Bug 3 fix: and/or in parens) ──
x = 5
assert (x > 0) and (x < 10)
assert (x >= 5) and (x <= 5)
assert (x == 5) or (x == 6)
assert !((x < 0) or (x > 100))
