---
applyTo: '*.py'
---
Role Definition:
 - Python Language Expert
 - Software Architect
 - Code Quality Specialist

General:
  Description: >
    Python code should be written to maximize readability, maintainability, and correctness
    while minimizing complexity and coupling. Adhere to PEP 8 and leverage Python's
    features to create clean, effective code.
  Requirements:
    - Write clear, self-documenting code.
    - Keep abstractions simple and focused.
    - Minimize dependencies and coupling.
    - Follow PEP 8 guidelines.

Repository Structure:
  - Keep the actual module at the root (e.g., `./sample/` or `./sample.py`), not in `src` or `python`.
  - Include `LICENSE` file at the root.
  - Include `setup.py` for package and distribution management at the root.
  - Include `requirements.txt` for development dependencies at the root.
  - Place documentation in `./docs/`.
  - Place tests in `./tests/` or `./test_sample.py`.
    - Use a `tests/context.py` for importing the module to be tested.
  - Consider a `Makefile` for generic management tasks at the root.
  - For Django projects, initialize with `django-admin.py startproject projectname .` to avoid unnecessary nesting.

Code Structure:
  - Avoid circular dependencies. If `moduleA` imports `moduleB`, `moduleB` should not import `moduleA`.
  - Minimize hidden coupling; changes in one module should not unexpectedly break unrelated modules.
  - Avoid heavy usage of global state or context. Pass necessary data explicitly.
  - Avoid spaghetti code (deeply nested conditionals/loops). Python's indentation helps, but aim for clarity.
  - Avoid ravioli code (too many small, similar classes/objects without clear structure).

Modules:
  - Use modules as the primary abstraction layer to separate concerns.
  - Keep module names short, lowercase, and avoid special symbols (e.g., `.`, `?`, `-`).
  - Do not use underscores to namespace (e.g., `my_module_utils.py`); use submodules/subpackages instead (e.g., `my_module/utils.py`).
  - Prefer `import module` and use `module.member` for clarity.
    ```python
    # Best
    import modu
    # ...
    x = modu.sqrt(4)

    # Better
    from modu import sqrt
    # ...
    x = sqrt(4)

    # Avoid (generally considered bad practice)
    from modu import *
    # ...
    x = sqrt(4)
    ```

Packages:
  - Any directory with an `__init__.py` file is a package.
  - `__init__.py` is executed when the package or a module within it is imported. It can be used to gather package-wide definitions.
  - Adding too much code to `__init__.py` files can slow down imports, especially in deeply nested package structures.
  - Leaving an `__init__.py` file empty is normal and good practice if modules/sub-packages don't need to share code.
  - Use `import very.deep.module as mod` for convenience with deeply nested packages.

Object-Oriented Programming (OOP):
  - Everything in Python is an object.
  - Python does not impose OOP as the main paradigm. Procedural or functional approaches are also viable.
  - Use classes when you need to bundle state and functionality.
  - Be cautious with stateful objects in concurrent environments (e.g., web applications) due to potential race conditions.
  - Prefer stateless functions where possible.
  - Pure functions (no side-effects, deterministic output for given input) are easier to test, refactor, and reason about.
    ```python
    # Good: Pure function
    def calculate_total(items):
        # ... logic ...
        return total

    # Avoid: Function with side-effects if purity is achievable
    global_order_total = 0
    def add_to_order_total(item_price):
        global global_order_total
        global_order_total += item_price
    ```

Decorators:
  - Use `@decorator` syntax to wrap functions or methods for concerns like caching, logging, or access control.
    ```python
    def my_decorator(func):
        def wrapper(*args, **kwargs):
            print("Something is happening before the function is called.")
            result = func(*args, **kwargs)
            print("Something is happening after the function is called.")
            return result
        return wrapper

    @my_decorator
    def say_whee():
        print("Whee!")

    say_whee()
    ```

Context Managers:
  - Use the `with` statement for managing resources that need setup and teardown (e.g., files, network connections, locks).
  - Implement custom context managers using a class with `__enter__` and `__exit__` methods, or more tersely with `@contextmanager` from `contextlib` and a generator.
    ```python
    # Class-based
    class CustomOpen(object):
        def __init__(self, filename):
            self.file = open(filename)

        def __enter__(self):
            return self.file

        def __exit__(self, ctx_type, ctx_value, ctx_traceback):
            self.file.close()

    with CustomOpen('file.txt') as f:
        contents = f.read()

    # Generator-based
    from contextlib import contextmanager

    @contextmanager
    def custom_open_generator(filename):
        f = open(filename)
        try:
            yield f
        finally:
            f.close()

    with custom_open_generator('file.txt') as f:
        contents = f.read()
    ```

Dynamic Typing:
  - Variables are names pointing to objects; they don't have a fixed type.
  - Avoid reusing the same variable name for different types of objects within the same scope.
    ```python
    # Bad
    items = "a b c"
    items = items.split(' ') # now a list
    items = set(items)     # now a set

    # Good
    item_string = "a b c"
    item_list = item_string.split(' ')
    item_set = set(item_list)
    ```
  - Consider functional programming practices like not reassigning variables if it enhances clarity, though Python doesn't enforce this (no `final` keyword).

Mutable and Immutable Types:
  - Mutable types (e.g., `list`, `dict`) can be changed in-place.
  - Immutable types (e.g., `tuple`, `str`, `int`, `frozenset`) cannot be changed in-place; operations create new objects.
    ```python
    my_list = [1, 2, 3]
    my_list[0] = 4  # my_list is now [4, 2, 3]

    my_tuple = (1, 2, 3)
    my_tuple[0] = 4  # This would raise a TypeError
    new_tuple = (4,) + my_tuple[1:] # new_tuple is (4, 2, 3)
    ```
  - Mutable types cannot be used as dictionary keys because their hash value can change.
  - Use mutable types for data that changes, immutable types for fixed data.
  - Strings are immutable. Building strings by repeated concatenation (`+=`) is inefficient.
    ```python
    # Bad: String concatenation in a loop
    result_string = ""
    for item in my_list:
       result_string += str(item)

    # Good: Use list.append and then str.join()
    parts = []
    for item in my_list:
        parts.append(str(item))
    result_string = "".join(parts)

    # Best (often): List comprehension with str.join()
    result_string = "".join([str(item) for item in my_list])
    ```
  - For a pre-determined number of strings, `+` or f-strings / `str.format()` are fine and often faster than `join()`.
    ```python
    foo = "foo"
    bar = "bar"

    # Good
    foobar = foo + bar
    foobar_fstring = f"{foo}{bar}"
    foobar_format = "{}{}".format(foo, bar)

    # Avoid (generally, for building strings in loops):
    foo += "ooo" # Inefficient if done repeatedly
    # Better for repeated appends:
    foo_parts = [foo, "ooo"]
    foo = "".join(foo_parts)
    ```
  - Prefer f-strings or `str.format()` over the older `%` operator for string formatting.
    ```python
    name = "World"
    # Best (f-string, Python 3.6+)
    greeting = f"Hello, {name}!"

    # Good (str.format)
    greeting = "Hello, {}!".format(name)

    # Avoid (older %-formatting)
    greeting = "Hello, %s!" % name
    ```

Type Hinting (PEP 484):
  - Use type hints to improve code clarity and allow static analysis.
    ```python
    def greet(name: str) -> str:
        return f"Hello, {name}"

    from typing import List, Dict, Tuple, Optional

    def process_data(data: List[Dict[str, int]], threshold: Optional[int] = None) -> Tuple[int, List[str]]:
        # ...
        return count, messages
        pass # Placeholder
    ```
  - Type hints are not enforced at runtime by default but can be checked by tools like MyPy.

Error Handling:
  - Use `try...except` blocks for handling exceptions.
  - Be specific in `except` clauses (e.g., `except ValueError:` rather than `except Exception:`).
  - Use `finally` for cleanup code that must always execute.
  - Use `else` in a `try` block for code that should run only if no exceptions were raised in the `try` part.
    ```python
    try:
        value = int(input("Enter a number: "))
    except ValueError:
        print("That was not a valid number.")
    else:
        print(f"You entered {value}.")
    finally:
        print("Execution finished.")
    ```
  - Consider defining custom exceptions for application-specific errors.
    ```python
    class MyAppError(Exception):
        pass

    raise MyAppError("Something specific went wrong in the app")
    ```

Testing:
  - Write tests for your code (e.g., using `unittest` or `pytest`).
  - Aim for high test coverage.
  - Separate tests from application code (e.g., in a `tests/` directory).
  - Design code for testability (e.g., pure functions, dependency injection).
