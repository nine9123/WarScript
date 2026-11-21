# T3.8 (PROP-27): a method's bare names are globals, never fields (T3.4), so
# reading a field by its bare name is an undefined variable: use @hp.
class Unit [hp]
    fun health []
        return hp   # expect-compile-error: undefined variable 'hp'
    end
end
