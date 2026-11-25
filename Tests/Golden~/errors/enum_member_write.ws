# An enum's members are constant (T3.8, PROP-28): a property write to the
# enum's instance is a TypeError, so the compiler can type `E.A` as a
# number. Reading them, and writing into the arrays they hold, still works.
enum Rank
    LOW
    HIGH = 5
end
r = Rank
begin
    r.LOW = "low"
rescue e
    print e
end
print Rank.LOW
print Rank.HIGH + Rank.count
r.HIGH += 1   # expect-error: TypeError: Cannot assign to 'HIGH' of enum 'Rank': enum members are constant
# expect: Cannot assign to 'LOW' of enum 'Rank': enum members are constant
# expect: 0
# expect: 7
