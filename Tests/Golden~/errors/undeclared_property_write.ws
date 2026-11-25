# T3.6: writing a property the instance does not declare raises NameError (PROP-15, plan 5.5).
# Another class declares it, so only the VM can tell (T3.8, PROP-29).
class P(x)
end
class Q(z)
end
p = new P(1)
p.z = 4   # expect-error: NameError: 'z' is not a field of class 'P'
