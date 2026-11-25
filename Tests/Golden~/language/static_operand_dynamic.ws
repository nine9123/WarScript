# T3.8 (PROP-28): the compiler only knows the type of a literal, an
# interpolation, an array literal, null, true/false and an operator result.
# An operand from a variable or a call is not inferred: these compile and
# raise the runtime TypeError (each static case is a static_operand_*.ws).
# Indexing has no static case: the grammar only indexes a name.
class P(x)
    fun get()
        return @x
    end
end

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

t = "Sir Bob"
attempt(fun() return t - "Sir " end)            # expect: Cannot apply '-' to text and text
attempt(fun() return same([1]) + 2 end)        # expect: Cannot apply '+' to array and number
attempt(fun() return same("a") * "b" end)      # expect: Cannot apply '*' to text and text
attempt(fun() return (same(4) - 1) / same("2") end)   # expect: Cannot apply '/' to number and text
attempt(fun() return same(5) % null end)       # expect: Cannot apply '%' to number and null
attempt(fun() return -(same("x")) end)         # expect: Cannot apply '-' to text
attempt(fun() return same(true) < 3 end)       # expect: Cannot apply '<' to logical and number
attempt(fun() return same(5) << 3 end)         # expect: Cannot apply '<<' to number and number
attempt(fun() return same(5).x end)         # expect: Cannot access property 'x' on non-class value
attempt(fun() return same("p").get() end)  # expect: Cannot call method 'get' on non-class value

fun write_prop()
    a = [1]
    a.x = 2
end
attempt(fun() return write_prop() end)         # expect: Cannot set property 'x' on non-class value

# Operations the table accepts on known types compile and run.
print "Sir " + same("Bob")                       # expect: Sir Bob
print "ab" * 2                                    # expect: abab
print [1] + [2]                                   # expect: [1, 2]
print -(2 - 5)                                    # expect: 3
print 1 < 2 == true                               # expect: true
