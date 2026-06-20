# ── 1. Basic lambda stored in variable ──
double = fun [x] return x * 2 end
assert double [3] == 6
assert double [0] == 0

# ── 2. Lambda with multiple params ──
add = fun [a, b] return a + b end
assert add [3, 4] == 7

# ── 3. Lambda with no params ──
get_42 = fun [] return 42 end
assert get_42 [] == 42

# ── 4. Lambda passed as argument ──
fun apply [arr, func]
    result = {}
    loop item in arr
        result << func [item]
    end
    return result
end

doubled = apply [{1, 2, 3}, fun [x] return x * 2 end]
assert doubled{0} == 2
assert doubled{1} == 4
assert doubled{2} == 6

# ── 5. Lambda with multi-line body ──
process = fun [n]
    result = 0
    loop i in 0..n
        result = result + i
    end
    return result
end
assert process [5] == 10

# ── 6. Higher-order function: function returning a lambda ──
fun make_adder [n]
    return fun [x] return x + n end
end

# Note: n is a global when the lambda runs (no closures).
# make_adder sets n in the global scope, and the lambda reads it.
# This works for the last-set value of n.

# ── 7. Lambda stored in array ──
ops = {
    fun [a, b] return a + b end,
    fun [a, b] return a - b end,
    fun [a, b] return a * b end
}
f_add = ops{0}
f_sub = ops{1}
f_mul = ops{2}
assert f_add [10, 3] == 13
assert f_sub [10, 3] == 7
assert f_mul [10, 3] == 30

# ── 8. Lambda as callback ──
fun filter [arr, predicate]
    result = {}
    loop item in arr
        if predicate [item]
            result << item
        end
    end
    return result
end

evens = filter [{1, 2, 3, 4, 5, 6}, fun [x] return x % 2 == 0 end]
assert evens{0} == 2
assert evens{1} == 4
assert evens{2} == 6

# ── 9. Reassigning a lambda variable ──
f = fun [x] return x + 1 end
assert f [10] == 11
f = fun [x] return x * 10 end
assert f [10] == 100

# ── 10. Lambda inside conditional ──
fun pick_strategy [mode]
    if mode == "double"
        return fun [x] return x * 2 end
    else
        return fun [x] return x + 1 end
    end
end

strategy = pick_strategy ["double"]
assert strategy [5] == 10
strategy = pick_strategy ["inc"]
assert strategy [5] == 6

print "all lambda tests passed"
