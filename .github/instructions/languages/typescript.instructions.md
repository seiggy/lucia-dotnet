---
applyTo: '*.ts, *.tsx'
---
Role Definition:
 - TypeScript Language Expert
 - Software Architect
 - Code Quality Specialist

General:
  Description: >
    TypeScript code should be written to maximize readability, maintainability, and correctness
    while minimizing complexity and coupling. Leverage TypeScript's strong typing system,
    embrace modern JavaScript features, and follow established best practices.
  Requirements:
    - Write clear, self-documenting code with TSDoc for public APIs.
    - Keep abstractions simple and focused.
    - Minimize dependencies and coupling.
    - Enable and adhere to strict compiler options.
    - Use ESLint and Prettier for consistent code style and quality.

Compiler and Tooling:
  - Enable Strict Mode:
      ```json
      // tsconfig.json
      {
        "compilerOptions": {
          "strict": true,
          // Other strict flags often enabled with strict:
          // "noImplicitAny": true,
          // "strictNullChecks": true,
          // "strictFunctionTypes": true,
          // "strictBindCallApply": true,
          // "strictPropertyInitialization": true,
          // "noImplicitThis": true,
          // "alwaysStrict": true
        }
      }
      ```
  - Use Linters and Formatters:
    - Integrate ESLint with TypeScript support (e.g., `@typescript-eslint/parser`, `@typescript-eslint/eslint-plugin`).
    - Use Prettier for consistent code formatting.

Type Definitions:
  - Prefer `unknown` over `any`:
      ```typescript
      // Good: Use unknown for values with uncertain types
      function processData(data: unknown) {
        if (typeof data === 'string') {
          console.log(data.toUpperCase());
        }
        // ... more type checks as needed
      }

      // Avoid: Using any bypasses type checking and can hide errors
      function processDataAny(data: any) {
        console.log(data.toUpperCase()); // Potential runtime error if data is not a string
      }
      ```
  - Use `interface` for Public APIs and `type` for Others:
      ```typescript
      // Good: Interface for defining the shape of objects, especially for public APIs or when extension is expected
      export interface UserProfile {
        id: string;
        username: string;
        email?: string;
      }

      // Good: Type for utility types, unions, intersections, or when dealing with primitives/literals
      type UserId = string | number;
      type ComponentState = 'loading' | 'success' | 'error';
      type UserWithPermissions = UserProfile & { permissions: string[] };
      ```
  - Leverage Utility Types:
      ```typescript
      interface Todo {
        title: string;
        description: string;
        completed: boolean;
        createdAt: Date;
      }

      // Good: Use Partial to make all properties optional (e.g., for updates)
      type PartialTodo = Partial<Todo>;
      const todoToUpdate: PartialTodo = { description: "new description" };

      // Good: Use Readonly to make all properties readonly
      type ReadonlyTodo = Readonly<Todo>;
      const stableTodo: ReadonlyTodo = { title: "Stable", description: "Cannot change", completed: false, createdAt: new Date()};

      // Good: Use Pick to select specific properties
      type TodoPreview = Pick<Todo, "title" | "completed">;

      // Good: Use Omit to exclude specific properties
      type TodoCreation = Omit<Todo, "completed" | "createdAt">; // Assuming these are set later
      ```

Immutability:
  - Leverage `readonly` for Immutability:
      ```typescript
      // Good: Using readonly for properties in interfaces and types
      interface Point {
        readonly x: number;
        readonly y: number;
      }
      const p: Point = { x: 10, y: 20 };
      p.x = 5; // Error: Cannot assign to 'x' because it is a read-only property.

      type ImmutableConfig = {
        readonly apiKey: string;
        readonly endpoint: string;
      };

      // Good: Using ReadonlyArray<T> or readonly T[] for immutable arrays
      const immutableArray: ReadonlyArray<number> = [1, 2, 3];
      immutableArray.push(4); // Error: Property 'push' does not exist on type 'readonly number[]'.
      immutableArray[0] = 0; // Error: Index signature in type 'readonly number[]' only permits reading.

      const anotherImmutableArray: readonly number[] = [4, 5, 6];
      anotherImmutableArray.pop(); // Error: Property 'pop' does not exist on type 'readonly number[]'.
      ```

Code Organization:
  - Use Modules for Encapsulation:
    - Organize code into modules using ES6 `import`/`export` syntax.
    - Group related functionality within modules to maintain a clear structure.
  - Avoid Default Exports (Prefer Named Exports):
      ```typescript
      // Good: Using named exports promotes clarity and consistency
      // file: stringUtils.ts
      export function capitalize(str: string): string { /* ... */ }
      export function truncate(str: string, length: number): string { /* ... */ }

      // Usage:
      import { capitalize, truncate } from './stringUtils';

      // Avoid: Default exports can lead to inconsistent naming upon import
      // file: mainHelper.ts
      export default function mainHelper() { /* ... */ }
      // Usage:
      import myPreferredNameForHelper from './mainHelper'; // Name can vary
      ```
  - Namespaces (Use Sparingly):
    - Prefer ES modules for most code organization.
    - Namespaces can be useful for organizing large sets of related types or when dealing with global variables from external UMD libraries.
      ```typescript
      // Example: Grouping related validation types
      export namespace Validation {
        export interface StringValidator {
          isAcceptable(s: string): boolean;
        }
        export interface NumberValidator {
          isValid(n: number): boolean;
        }
      }
      const stringValidator: Validation.StringValidator = ...;
      const numberValidator: Validation.NumberValidator = ...;
      ```

Code Clarity:
  - Consistent Naming Conventions:
    - `PascalCase` for types (classes, interfaces, enums, type aliases).
    - `camelCase` for variables, functions, methods, and object properties.
    - `UPPER_CASE_SNAKE_CASE` for constants and enum values (though PascalCase is also common for enum members).
    - Consider prefixing interfaces with `I` (e.g., `IUserProfile`) if it's a strong team convention, but modern TypeScript often omits it.
  - Effective Commenting with TSDoc:
      ```typescript
      /**
       * Fetches user data from the API.
       * @param userId - The unique identifier of the user.
       * @returns A Promise resolving to the user's profile.
       * @throws {NetworkError} If the network request fails.
       * @example
       * ```ts
       * fetchUserData('123')
       *   .then(user => console.log(user))
       *   .catch(error => console.error(error));
       * ```
       */
      export async function fetchUserData(userId: string): Promise<UserProfile> {
        // ... implementation ...
        return {} as UserProfile; // Placeholder
      }
      ```

Error Handling:
  - Use Custom Error Classes:
      ```typescript
      // Good: Custom error classes for better error identification and handling
      export class NetworkError extends Error {
        constructor(message: string, public statusCode?: number) {
          super(message);
          this.name = "NetworkError";
          // Set the prototype explicitly for ES5 and older environments if needed
          Object.setPrototypeOf(this, NetworkError.prototype);
        }
      }

      async function fetchData(url: string): Promise<any> {
        const response = await fetch(url);
        if (!response.ok) {
          throw new NetworkError(`HTTP error! Status: ${response.status}`, response.status);
        }
        return response.json();
      }

      async function process() {
        try {
          const data = await fetchData("https://api.example.com/data");
          console.log(data);
        } catch (error) {
          if (error instanceof NetworkError) {
            console.error(`Network error: ${error.message}, Status Code: ${error.statusCode}`);
          } else if (error instanceof Error) {
            console.error(`Generic error: ${error.message}`);
          } else {
            console.error("An unknown error occurred:", error);
          }
        }
      }
      ```
  - Handle Promise Rejections Consistently:
    - Always attach a `.catch()` handler to promises or use `try/catch` blocks with `async/await` to manage potential rejections.

Asynchronous Programming:
  - Embrace `async/await` for Clarity:
      ```typescript
      // Good: Using async/await for cleaner and more readable asynchronous code
      async function getUserAndPosts(userId: string): Promise<{ user: UserProfile; posts: Post[] }> {
        try {
          const user = await fetchUserData(userId); // Assumes fetchUserData is async
          const posts = await fetchPostsForUser(userId); // Assumes fetchPostsForUser is async
          return { user, posts };
        } catch (error) {
          console.error("Failed to get user data and posts:", error);
          throw error; // Re-throw or handle appropriately by returning a default/error state
        }
      }
      ```
  - Type Promises Explicitly:
    - Always specify the resolved type of a Promise, e.g., `Promise<string>`, `Promise<void>`, `Promise<UserProfile>`.
    - For functions returning promises, ensure the return type annotation is `Promise<T>`.

Dependency Management:
  - Consider Dependency Injection (DI) Principles:
    - For larger applications, apply DI principles to manage dependencies. This promotes loose coupling, making code more modular and testable.
    - Pass dependencies (services, configurations, etc.) as constructor parameters or method arguments, often typed with interfaces.
      ```typescript
      interface ILogger {
        log(message: string): void;
        error(message: string, error?: any): void;
      }

      interface IApiClient {
        get<T>(endpoint: string): Promise<T>;
      }

      class UserService {
        constructor(private logger: ILogger, private apiClient: IApiClient) {}

        async getUser(id: string): Promise<UserProfile | null> {
          try {
            const user = await this.apiClient.get<UserProfile>(`/users/${id}`);
            this.logger.log(`User ${id} fetched successfully.`);
            return user;
          } catch (error) {
            this.logger.error(`Error fetching user ${id}:`, error);
            return null; // Or re-throw, depending on desired error handling strategy
          }
        }
      }
      ```

Testing Considerations:
  - Design for Testability:
    - Write pure functions whenever possible, as they are easier to test.
    - Keep functions, classes, and modules small and focused on a single responsibility (SRP).
    - Use interfaces for dependencies to allow for easy mocking/stubbing in tests.
  - Write Comprehensive Unit Tests:
    - Utilize testing frameworks like Jest, Mocha, Vitest, or Playwright for component/e2e tests.
    - Aim for good test coverage, focusing on critical logic paths, edge cases, and error conditions.