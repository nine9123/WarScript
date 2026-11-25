# T3.12: '::' is no longer the member operator.
class P(x)
end
p = new P(1)
print p :: x   # expect-compile-error: Unexpected '::' at line 5: members are written obj.name
