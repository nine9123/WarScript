# ──────────────────────────────────────────────────────
# test_gapfill_equality_symmetry.ws
# Gap: every existing != test compared values that ARE
# different. No test checked that (a == b) implies
# !(a != b) for arrays, classes, or nested structures.
# ──────────────────────────────────────────────────────

# ════════════════════════════════════════════════
#  Core invariant: (a == b) ⟺ !(a != b)
# ════════════════════════════════════════════════

# ── Numeric ──
assert 42 == 42
assert !(42 != 42)
assert !(1 == 2)
assert 1 != 2

# ── String ──
assert "hello" == "hello"
assert !("hello" != "hello")
assert !("a" == "b")
assert "a" != "b"

# ── Boolean ──
assert true == true
assert !(true != true)
assert false == false
assert !(false != false)
assert !(true == false)
assert true != false

# ── Null ──
assert null == null
assert !(null != null)
assert !(null == 0)
assert null != 0
assert !(0 == null)
assert 0 != null
assert !(null == "")
assert null != ""
assert !(null == false)
assert null != false

# ════════════════════════════════════════════════
#  Arrays — this was the main gap
# ════════════════════════════════════════════════

# ── Equal arrays ──
a = {1, 2, 3}
b = {1, 2, 3}
assert a == b
assert !(a != b)

# ── Different arrays ──
c = {1, 2, 4}
assert !(a == c)
assert a != c

# ── Different length arrays ──
d = {1, 2}
assert !(a == d)
assert a != d

# ── Empty arrays ──
e1 = {}
e2 = {}
assert e1 == e2
assert !(e1 != e2)

# ── Single element ──
s1 = {42}
s2 = {42}
assert s1 == s2
assert !(s1 != s2)

# ── Nested arrays ──
n1 = {{1, 2}, {3, 4}}
n2 = {{1, 2}, {3, 4}}
assert n1 == n2
assert !(n1 != n2)

n3 = {{1, 2}, {3, 5}}
assert !(n1 == n3)
assert n1 != n3

# ── Arrays with strings ──
as1 = {"hello", "world"}
as2 = {"hello", "world"}
assert as1 == as2
assert !(as1 != as2)

# ── Arrays with booleans ──
ab1 = {true, false, true}
ab2 = {true, false, true}
assert ab1 == ab2
assert !(ab1 != ab2)

# ── Arrays with null ──
an1 = {null, 1, null}
an2 = {null, 1, null}
assert an1 == an2
assert !(an1 != an2)

# ── Mixed type arrays ──
m1 = {1, "two", true, null}
m2 = {1, "two", true, null}
assert m1 == m2
assert !(m1 != m2)

# ════════════════════════════════════════════════
#  Classes — also a major gap
# ════════════════════════════════════════════════

class Point[x, y]
end

# ── Equal instances ──
p1 = new Point[10, 20]
p2 = new Point[10, 20]
assert p1 == p2
assert !(p1 != p2)

# ── Different instances ──
p3 = new Point[10, 30]
assert !(p1 == p3)
assert p1 != p3

# ── Zero-value instances ──
z1 = new Point[0, 0]
z2 = new Point[0, 0]
assert z1 == z2
assert !(z1 != z2)

# ── Null property instances ──
np1 = new Point[null, null]
np2 = new Point[null, null]
assert np1 == np2
assert !(np1 != np2)

# ── String property class ──
class Name[first, last]
end
nm1 = new Name["John", "Doe"]
nm2 = new Name["John", "Doe"]
assert nm1 == nm2
assert !(nm1 != nm2)

nm3 = new Name["Jane", "Doe"]
assert !(nm1 == nm3)
assert nm1 != nm3

# ════════════════════════════════════════════════
#  Class with inheritance
# ════════════════════════════════════════════════

class Animal[name]
end

class Dog[name] : Animal[name]
end

d1 = new Dog["Rex"]
d2 = new Dog["Rex"]
assert d1 == d2
assert !(d1 != d2)

d3 = new Dog["Buddy"]
assert !(d1 == d3)
assert d1 != d3

# ════════════════════════════════════════════════
#  Cross-type comparisons
# ════════════════════════════════════════════════

# ── String representation fallback ──
assert 42 == "42"
assert !(42 != "42")
assert !("42" != 42)

# ── Different types that differ ──
assert 42 != "43"
assert "43" != 42
assert !("43" == 42)

# ════════════════════════════════════════════════
#  Equality used in control flow
# ════════════════════════════════════════════════

# ── If condition with != on equal arrays ──
arr1 = {1, 2, 3}
arr2 = {1, 2, 3}
entered = false
if arr1 != arr2
    entered = true
end
assert entered == false

# ── Loop condition with != ──
p = new Point[0, 0]
target = new Point[0, 0]
count = 0
loop p != target
    count += 1
    break
end
# p == target so loop should never execute
assert count == 0
