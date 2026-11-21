# ──────────────────────────────────────────────────────
# test_regression_equals_symmetry.ws
# Bug: EqualsOperator uses left.Equals(right) (structural)
#      but NotEqualsOperator uses GetObjectValue().Equals()
#      (inner value), so (a == b) and !(a != b) can
#      disagree for class instances and arrays.
# ──────────────────────────────────────────────────────

# ── Array equality / not-equality must be symmetric ──
a = {1, 2, 3}
b = {1, 2, 3}
assert a == b
assert !(a != b)

c = {1, 2, 4}
assert !(a == c)
assert a != c

# ── Empty arrays ──
e1 = {}
e2 = {}
assert e1 == e2
assert !(e1 != e2)

# ── Nested arrays ──
n1 = {{1, 2}, {3, 4}}
n2 = {{1, 2}, {3, 4}}
assert n1 == n2
assert !(n1 != n2)

# ── Class instance equality must be symmetric (identity, plan 5.4) ──
class Point [x, y]
end

p1 = new Point[10, 20]
p2 = new Point[10, 20]
assert !(p1 == p2)
assert !(p2 == p1)
assert p1 != p2
p4 = p1
assert p1 == p4
assert p4 == p1

p3 = new Point[10, 30]
assert !(p1 == p3)
assert p1 != p3

# ── Mixed type comparisons: different types are not equal (DEC-8) ──
assert 42 != "42"
assert !(42 == "42")

# ── Null symmetry ──
assert null == null
assert !(null != null)

val = 5
assert !(val == null)
assert val != null
