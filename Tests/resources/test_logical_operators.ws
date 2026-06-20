# ──────────────────────────────────────────────────────
# test_logical_operators.ws
# Covers: and, or, !, short-circuit evaluation,
#         compound boolean expressions, De Morgan's laws,
#         !(expr), !!x, (expr and/or expr) in parens
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

# ── Double not (Bug 2 fix: consecutive unary operators) ──
assert !!true
assert !!false == false
dbl_neg = !!false
assert !dbl_neg

# ── Not with parenthesized expression (Bug 1 fix) ──
assert !(5 < 3)
assert !(5 == 6)
assert !(false)
assert !("a" == "b")

# ── Parenthesized and/or (Bug 3 fix) ──
assert (true and true)
assert (true or false)
assert !(false and true)
assert !(false or false)
assert (5 > 3 and 10 > 7)
assert (5 > 3 or 10 < 7)
assert !(5 < 3 and 10 > 7)
assert (5 < 3 or 10 > 7)

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
assert !(x < 0 or x > 10)

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

# ── De Morgan's laws (using parens — Bug 1+3 fix) ──
a = true
b = false
assert !(a and b) == (!a or !b)
assert !(a or b) == (!a and !b)

a = true
b = true
assert !(a and b) == (!a or !b)
assert !(a or b) == (!a and !b)

a = false
b = false
assert !(a and b) == (!a or !b)
assert !(a or b) == (!a and !b)

# ── Boolean in if conditions ──
flag = true
matched = false
if flag and 10 > 5
    matched = true
end
assert matched

matched2 = false
if !flag or 10 < 5
    matched2 = true
end
assert !matched2
