# T3.3 (PROP-16): a lambda has no `this`, so neither `this` nor `@x` may be
# used in one, even inside a method.
class P [x]
    fun f []
        g = fun [] return @x end   # expect-compile-error: 'this' and '@' fields cannot be used in a lambda
        return g []
    end
end
