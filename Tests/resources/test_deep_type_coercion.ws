# Type coercion, dynamic typing, mixed-type operations,
# edge cases with null, booleans, numbers, strings

# ── 1. String + number coercion ──
assert "value: " + 0 == "value: 0"
assert "value: " + -5 == "value: -5"
assert "value: " + 3.14 == "value: 3.14"
assert "value: " + true == "value: True"
assert "value: " + false == "value: False"
assert "value: " + null == "value: null"
assert 42 + " items" == "42 items"
assert 0 + " zero" == "0 zero"
assert -1 + " neg" == "-1 neg"

# ── 2. Numeric precision ──
assert 0.1 + 0.2 > 0.29
assert 0.1 + 0.2 < 0.31
assert 1000000 * 1000000 == 1000000000000
assert 1 / 3 + 1 / 3 + 1 / 3 == 1
assert 10 / 3 * 3 == 10

# ── 3. Integer display (no trailing .0) ──
x = 10.0
assert x + "" == "10"
y = 10.5
assert y + "" == "10.5"

# ── 4. Boolean in various contexts ──
assert true == true
assert false == false
assert true != false
assert true != 1
assert false != 0
# Cross-type == uses ToString() fallback, so true == "True"
assert true == "True"
assert false == "False"

# ── 5. Null comparisons exhaustive ──
assert null == null
assert null != 0
assert null != false
assert null != ""
assert null != true
assert null != "null"
assert 0 != null
assert false != null
assert "" != null

# ── 6. Dynamic variable types ──
x = 42
assert x == 42
x = "hello"
assert x == "hello"
x = true
assert x == true
x = null
assert x == null
x = {1, 2}
assert x == {1, 2}

# ── 7. Mixed array types ──
arr = {1, "two", true, null, 3.14, {5, 6}}
assert arr{0} == 1
assert arr{1} == "two"
assert arr{2} == true
assert arr{3} == null
assert arr{4} == 3.14
assert arr{5} == {5, 6}

# ── 8. String to string via concatenation ──
assert "" + 42 == "42"
assert "" + true == "True"
assert "" + false == "False"
assert "" + null == "null"
assert "" + 3.14 == "3.14"

# ── 9. Cross-type equality uses string fallback ──
# When types differ and aren't null, == compares ToString()
assert 5 == "5"
assert true == "True"
assert false == "False"
assert 3.14 == "3.14"
# But null doesn't use string fallback (reference compare)
assert null != "null"

# ── 10. Array equality deep ──
assert {1, {2, 3}} == {1, {2, 3}}
assert {1, {2, 3}} != {1, {3, 2}}
assert {"a", "b"} == {"a", "b"}
assert {"a", "b"} != {"b", "a"}
assert {null} == {null}
assert {true, false} == {true, false}
