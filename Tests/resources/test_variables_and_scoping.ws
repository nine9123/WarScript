# ──────────────────────────────────────────────────────
# test_variables_and_scoping.ws
# Covers: assignment, null default, scoping in if/loop/
#         function, variable reassignment
# ──────────────────────────────────────────────────────

# ── Basic assignment ──
x = 10
assert x == 10

y = "hello"
assert y == "hello"

z = true
assert z == true

n = null
assert n == null

# ── Reassignment ──
a = 1
assert a == 1
a = 2
assert a == 2
a = "now a string"
assert a == "now a string"

# ── Assignment from expressions ──
b = 3 + 4
assert b == 7

c = b * 2
assert c == 14

d = c > 10
assert d == true

# ── Uninitialized variables are null ──
assert uninitialized_var == null

# ── Variables persist across if blocks (if pre-declared) ──
value = 1
if true
    value = 2
end
assert value == 2

# ── Variable set in if (pre-declared) ──
result = "none"
if true
    result = "yes"
end
assert result == "yes"

# ── Variables in else branch (pre-declared) ──
condition = false
result2 = "none"
if condition
    result2 = "yes"
else
    result2 = "no"
end
assert result2 == "no"

# ── Variables persist across loops ──
total = 0
loop i in 0..5
    total = total + i
end
assert total == 10

# ── Nested scopes: outer visible in inner ──
outer = 1
mid_check = false
if true
    assert outer == 1
    mid_check = true
end
assert mid_check

# ── Function scope ──
global_var = 100
fun read_global []
    return global_var
end
assert read_global [] == 100

# ── Function can modify outer variables ──
counter = 0
fun increment_counter []
    counter = counter + 1
end
increment_counter []
increment_counter []
increment_counter []
assert counter == 3

# ── Multiple variables ──
v1 = 1
v2 = 2
v3 = 3
v4 = 4
v5 = 5
assert v1 + v2 + v3 + v4 + v5 == 15

# ── Swap variables ──
p = 10
q = 20
temp = p
p = q
q = temp
assert p == 20
assert q == 10

# ── Variable used in its own reassignment ──
w = 5
w = w + 1
assert w == 6
w = w * w
assert w == 36

# ── Null assignments ──
something = 42
assert something == 42
something = null
assert something == null

# ── Boolean variables (avoid parens around and/or) ──
flag1 = true
flag2 = false
assert flag1 and !flag2
assert flag1 or flag2
both = flag1 and flag2
assert !both
