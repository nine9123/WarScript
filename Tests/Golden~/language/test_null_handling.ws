# ──────────────────────────────────────────────────────
# test_null_handling.ws
# Covers: null literal, null comparisons, null in arrays,
#         null class properties, null function returns,
#         null assignment, null propagation
# ──────────────────────────────────────────────────────

# ── Null literal ──
x = null
assert x == null

# ── Null equality ──
assert null == null

# ── Null vs other types ──
assert null != 0
assert null != false
assert null != ""
assert null != true
assert 0 != null
assert false != null
assert "" != null

# ── Uninitialized is null ──
assert unset_variable == null

# ── Null assignment ──
a = 42
assert a == 42
a = null
assert a == null

# ── Null in arrays ──
arr = {1, null, 3}
assert arr{0} == 1
assert arr{1} == null
assert arr{2} == 3

# ── Null appended to array ──
arr2 = {}
arr2 << null
arr2 << 1
arr2 << null
assert arr2{0} == null
assert arr2{1} == 1
assert arr2{2} == null

# ── Null in class properties ──
class Container [value]
end
c = new Container [null]
assert c :: value == null

c :: value = 42
assert c :: value == 42

c :: value = null
assert c :: value == null

# ── Class with optional property ──
class User [name]
end
u = new User ["Alice"]
assert u :: name == "Alice"

# ── Null check pattern ──
fun safe_get [container]
    if container :: value != null
        return container :: value
    end
    return "default"
end
c1 = new Container [null]
c2 = new Container [42]
assert safe_get [c1] == "default"
assert safe_get [c2] == 42

# ── Null in conditions (pre-declare results) ──
val = null
result = "unset"
if val == null
    result = "was null"
else
    result = "had value"
end
assert result == "was null"

val = 42
result2 = "unset"
if val != null
    result2 = "has value"
else
    result2 = "was null"
end
assert result2 == "has value"

# ── Function returning null explicitly ──
fun return_null
    return null
end
assert return_null [] == null

# ── Function returning null implicitly (empty return) ──
fun return_nothing
    return
end
assert return_nothing [] == null

# ── Null equality checks ──
assert (null == null) == true
assert (null != 0) == true
assert (null != false) == true
assert (null != "") == true

# ── Null in array position check ──
arr = {1, null, 3, null, 5}
null_count = 0
loop i in 0..5
    if arr{i} == null
        null_count += 1
    end
end
assert null_count == 2

# ── Null assignment in loop ──
arr = {1, 2, 3, 4, 5}
loop i in 0..5
    if i % 2 == 0
        arr{i} = null
    end
end
assert arr{0} == null
assert arr{1} == 2
assert arr{2} == null
assert arr{3} == 4
assert arr{4} == null

# ── Null class property in interpolation ──
obj = new Container [null]
s = "value: {obj :: value}"
assert s == "value: null"

# ── Setting property to null ──
class Entity [name, target]
end
e = new Entity ["hero", "enemy"]
assert e :: target == "enemy"
e :: target = null
assert e :: target == null
