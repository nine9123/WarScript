# ──────────────────────────────────────────────────────
# test_logical_operators.ws
# Covers: and, or, !, short-circuit evaluation,
#         compound boolean expressions, De Morgan's laws
# ──────────────────────────────────────────────────────

# ── Basic and ──
assert true and true
t_and_f = true and false
assert !t_and_f
f_and_t = false and true
assert !f_and_t
f_and_f = false and false
assert !f_and_f

# ── Basic or ──
assert true or true
assert true or false
assert false or true
f_or_f = false or false
assert !f_or_f

# ── Not operator ──
assert !false
assert !false == true
assert !true == false

# ── Double not (via intermediate — !! crashes the shunting-yard parser) ──
not_true = !true
assert !not_true
not_false = !false
assert not_false

# ── Not with comparison (use intermediate var, !(expr) crashes parser) ──
ne = 5 == 6
assert !ne
gt = 5 > 10
assert !gt
lt = 5 < 3
assert !lt
se = "a" == "b"
assert !se

# ── Compound expressions ──
assert true and true and true
c1 = true and true and false
assert !c1
assert false or false or true
c2 = false or false or false
assert !c2

# ── Mixed and / or ──
assert true and true or false
assert false or true and true

# ── And / or with comparisons ──
x = 5
assert x > 0 and x < 10
assert x == 5 or x == 6
assert x >= 5 and x <= 5
assert x > 4 and x < 6

# ── Short-circuit: and skips right when left is false ──
side_effect_ran = false
fun set_side_effect []
    side_effect_ran = true
    return true
end
result = false and set_side_effect []
assert !side_effect_ran

# ── Short-circuit: or skips right when left is true ──
side_effect_ran2 = false
fun set_side_effect2 []
    side_effect_ran2 = true
    return false
end
result2 = true or set_side_effect2 []
assert !side_effect_ran2

# ── Short-circuit: and evaluates right when left is true ──
side_effect_ran3 = false
fun set_side_effect3 []
    side_effect_ran3 = true
    return true
end
result3 = true and set_side_effect3 []
assert side_effect_ran3

# ── Short-circuit: or evaluates right when left is false ──
side_effect_ran4 = false
fun set_side_effect4 []
    side_effect_ran4 = true
    return true
end
result4 = false or set_side_effect4 []
assert side_effect_ran4

# ── De Morgan's laws ──
a = true
b = false
lhs1 = a and b
rhs1 = !a or !b
assert !lhs1 == rhs1
lhs2 = a or b
rhs2 = !a and !b
assert !lhs2 == rhs2

a = true
b = true
lhs3 = a and b
rhs3 = !a or !b
assert !lhs3 == rhs3
lhs4 = a or b
rhs4 = !a and !b
assert !lhs4 == rhs4

a = false
b = false
lhs5 = a and b
rhs5 = !a or !b
assert !lhs5 == rhs5
lhs6 = a or b
rhs6 = !a and !b
assert !lhs6 == rhs6

# ── Boolean in if conditions ──
flag = true
matched = false
if flag and 10 > 5
    matched = true
end
assert matched

matched2 = false
not_flag = !flag
if not_flag or 10 < 5
    matched2 = true
end
assert !matched2
