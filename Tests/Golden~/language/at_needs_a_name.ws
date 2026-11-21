# T3.3: `@` must be followed directly by a field name.
class P [x]
    fun f []
        return @ x   # expect-compile-error: '@' must be followed by a field name
    end
end
