struct Person
    arg name
    arg experience
    arg is_developer
end

if false! then
    print "test"
end

struct Person2
    arg name
    arg experience
    arg is_developer
end

result = 10 + 20 + 5 - 40
print result

person = new Person ["SomeGuy" 15 true]
print person

human = new Person2 ["Jimmy" 45 false]
print human

is_jimmy_not_a_developer = human :: is_developer
print is_jimmy_not_a_developer

if human :: is_developer == false then
    print "jimmy is not a developer"
end

human2 = new Person ["Oleg" 15 true]
print person

if person == person then
    print "is equal"
end

if person == human then
    print "is equal 2"
end

if person == human2 then
    print "is equal 3"
end

if 10 > 5 then
    print "10 is greater than 5"
end

if 5 < 10 then
    print "5 is less than 10"
end

if person :: is_developer then

    person_name = person :: name
    print "hey " + person_name + "!"

    experience = person :: experience

    if experience > 0 then
        started_in = 2025 - experience
        print "you had started your career in " + started_in
    end

end