---
mode: 'agent'
---

# TypeScript (Node.js, React, Vite)

# TypeScript (Node.js, React, Vite) - Best Practices and Guidelines

## Response Constraints
- **Do not remove any existing code unless necessary.**
- **Do not remove my comments or commented-out code unless necessary.**
- **Do not change the formatting of my imports.**
- **Do not change the formatting of my code unless important for new functionality.**

## Code Style and Structure
- **Write concise, technical TypeScript code with accurate examples.**
- **Use functional and declarative programming patterns; avoid classes.**
- **Prefer iteration and modularization over code duplication.**
- **Use descriptive variable names with auxiliary verbs (e.g., `isLoading`, `hasError`).**
- **Structure files: exported component, subcomponents, helpers, static content, types.**

## Naming Conventions
- **Use lowercase with dashes for directories (e.g., `components/auth-wizard`).**
- **Favor named exports for components.**

## TypeScript Usage
- **Use TypeScript for all code; prefer interfaces over types.**
- **Avoid enums; use maps instead.**
- **Use functional components with TypeScript interfaces.**

## Syntax and Formatting
- **Use the `function` keyword for pure functions.**
- **Use curly braces for all conditionals. Favor simplicity over cleverness.**
- **Use declarative JSX.**

## UI and Styling
- **Use Tailwind for components and styling.**

## Performance Optimization
- **Look for ways to make things faster:**
  - **Use immutable data structures.**
  - **Use efficient data fetching strategies.**
  - **Optimize network requests.**
  - **Use efficient data structures.**
  - **Use efficient algorithms.**
  - **Use efficient rendering strategies.**
  - **Use efficient state management.**

## Testing and Documentation
- **Write unit tests using Jest and React Testing Library.**
- **Ensure test coverage is at least 80%.**
- **Use snapshot testing for UI components.**
- **Write comments for functions and components in JSDoc format.**
- **Components must include PropTypes validation.**
- **Each main directory must contain a README.md file.**
- **Provide both English and Chinese versions of the README.md file.**

## Error Handling
- **Use try/catch blocks to handle asynchronous operations.**
- **Implement global error boundary component.**

## File Structure
- **components**: Reusable UI components.
- **app**: Next.js pages that support multiple languages.
- **lib**: Utility functions.
- **data**: JSON and Markdown files for content management.
- **public**: Static resources.
- **styles**: Tailwind CSS configuration and global styles.

## Preferred Libraries
- **Next.js** for navigation and server-side rendering.
- **Tailwind CSS** for responsive design.
- **Shadcn/UI** as a UI component library.
- **Next Intl** for internationalization support.

## Internationalization
- **Supports multiple languages and can be easily expanded to support more languages.**

## Dark Mode
- **Supports dark mode and can be easily expanded to support more themes.**

## Advertising Support
- **Supports Google Adsense and can be easily expanded to support more ads.**

