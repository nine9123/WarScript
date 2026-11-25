# T3.8 (PROP-28): range-loop bounds whose types are not known compile, and
# the step is never checked (static_operand_range.ws is the static case).
fun same(v)
    return v
end

fun upper_from_call()
    loop i in 0..same("3")
        print i
    end
end

fun text_step()
    loop i in 0..2 by "x"
        print i
    end
end

fun attempt(f)
    begin
        f()
    rescue e
        print e
    end
end

attempt(fun() return upper_from_call() end)   # expect: Cannot apply '<' to number and text
attempt(fun() return text_step() end)         # expect: 0
                                                 # expect: Cannot apply '<' to text and number
loop i in "a".."b"
    print i                                      # expect: a
    break
end
