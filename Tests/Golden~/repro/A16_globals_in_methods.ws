# Plan A.16 (PROP-20). v1 prints null.
g = 5
class C
    fun m []
        return g
    end
end
print (new C) :: m []   # expect: 5
