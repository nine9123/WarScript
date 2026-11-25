# T3.6 (DEC-8, PROP-6, PROP-7, PROP-17, plan 5.3): the strict operator table.
# '+' concatenates when either operand is text, else adds two numbers, else
# joins two arrays; '-', '/', '%' and unary '-' take numbers only; '*' also
# repeats text; ordering takes two numbers or two texts (ordinal); '<<'
# needs an array. Anything else is TypeError "Cannot apply '<op>' to ...".
# '==' between different types is false and raises nothing (5.4).

# The operands of the failing cases come from a call: with literal operands
# the same operations are compile errors (T3.8, PROP-28; see
# static_operand_*.ws).
fun same(v)
    return v
end

fun attempt(f)
    begin
        print f()
    rescue e
        print e
    end
end

# ── + ──
print 1 + 2                                   # expect: 3
print "a" + 1                                 # expect: a1
print 1 + "a"                                 # expect: 1a
print "b=" + true                             # expect: b=true
print "n=" + null                             # expect: n=null
print "a" + [1, 2]                            # expect: a[1, 2]
print [1] + [2, 3]                            # expect: [1, 2, 3]
attempt(fun() return same([1, 2]) + 3 end) # expect: Cannot apply '+' to array and number
attempt(fun() return same(3) + [1, 2] end) # expect: Cannot apply '+' to number and array
attempt(fun() return same(true) + 1 end)   # expect: Cannot apply '+' to logical and number
attempt(fun() return same(null) + null end) # expect: Cannot apply '+' to null and null

# ── Interpolation is always text ──
a = 1
b = 2
print "{a}{b}"                                # expect: 12
print "{a}" == "1"                            # expect: true
print "{a > 0}"                               # expect: true

# ── - * / % ──
print 5 - 3                                   # expect: 2
attempt(fun() return same("abc") - "b" end) # expect: Cannot apply '-' to text and text
attempt(fun() return same(5) - "5" end)    # expect: Cannot apply '-' to number and text
print "ab" * 2                                # expect: abab
print 2 * "ab"                                # expect: abab
attempt(fun() return same("a") * "b" end)  # expect: Cannot apply '*' to text and text
attempt(fun() return same([1]) * 2 end)    # expect: Cannot apply '*' to array and number
attempt(fun() return same("6") / 2 end)    # expect: Cannot apply '/' to text and number
attempt(fun() return same(6) % false end)  # expect: Cannot apply '%' to number and logical

# ── Unary minus ──
print -(2)                                    # expect: -2
attempt(fun() return -(same("abc")) end)   # expect: Cannot apply '-' to text
attempt(fun() return -(same(true)) end)    # expect: Cannot apply '-' to logical
attempt(fun() return -(same(null)) end)    # expect: Cannot apply '-' to null

# ── Ordering ──
print 1 < 2                                   # expect: true
print "B" < "a"                               # expect: true
print "b" >= "a"                              # expect: true
attempt(fun() return same("10") < 9 end)   # expect: Cannot apply '<' to text and number
attempt(fun() return same(10) <= "9" end)  # expect: Cannot apply '<=' to number and text
attempt(fun() return same(true) > false end) # expect: Cannot apply '>' to logical and logical
attempt(fun() return same([1]) >= [2] end) # expect: Cannot apply '>=' to array and array
attempt(fun() return same(null) < 1 end)   # expect: Cannot apply '<' to null and number

# The fused compare-and-jump of a range loop (LessJump) checks its operands too.
fun count_to(n)
    c = 0
    loop i in 0..n
        c += 1
    end
    return c
end
print count_to(3)                            # expect: 3
attempt(fun() return count_to("3") end)    # expect: Cannot apply '<' to number and text

# ── Equality across types ──
print 1 == "1"                                # expect: false
print 1 != "1"                                # expect: true
print null == 0                               # expect: false
print null == false                           # expect: false
print true == 1                               # expect: false
print [1] == "[1]"                            # expect: false
print null == null                            # expect: true

# ── << ──
arr = [1]
print arr << 2                                # expect: [1, 2]
attempt(fun() return same(5) << 3 end)     # expect: Cannot apply '<<' to number and number
attempt(fun() return same("ab") << "c" end) # expect: Cannot apply '<<' to text and text

# ── Text_remove replaces text subtraction (PROP-7) ──
print Text_remove("banana", "na")            # expect: ba
print Text_remove("abc", "")                 # expect: abc
print Text_remove("abc", "x")                # expect: abc
