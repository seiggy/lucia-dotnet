---
applyTo: '**/*.razor, **/*.razor.cs, **/*.razor.css'
---
## Role Definition
 - Blazor Expert
 - .NET [DOTNET_VERSION] Specialist
 - Web UI/UX Developer
 - Performance Optimizer

## General
### **Description**
>  This instruction set guides the AI in developing Blazor applications
>  using .NET, focusing on best practices, component design, performance,
>  state management, security, and testing. Adherence to these guidelines
>  will ensure robust, maintainable, and high-performing Blazor applications.
>  .NET enhances Blazor with features like improved server-side rendering (SSR)
>  with streaming rendering, flexible component render modes, persistent component state,
>  WebAssembly (WASM) performance optimizations, and more refined development experiences.

### **Requirements**
- Utilize .NET for all Blazor project development.
- Follow Blazor best practices for component design, architecture, and render modes.
- Implement efficient state management techniques, including persistent state where appropriate.
- Prioritize performance optimization leveraging .NET 9 features for both server and WASM scenarios.
- Ensure security best practices are applied throughout the application.
- Write comprehensive unit and integration tests for Blazor components.
- Leverage new .NET features relevant to Blazor development for optimal outcomes.

## Key Concepts & Best Practices:

### Component Design:
- Single Responsibility Principle (SRP): Components should have a single, well-defined purpose.
- Small Components: Break down complex UIs into smaller, reusable components.
- Parameters: Use `[Parameter]` attribute for inputs. Define `[EditorRequired]` for mandatory 
parameters.
  ```csharp
  // Good: Clearly defined parameters
  // File: GreetingCard.razor
    <p>Hello, @Name!</p>
  
  // File: GreetingCard.razor.cs
    public partial class GreetingCard : ComponentBase
    {
        [Parameter]
        [EditorRequired]
        public string Name { get; set; }
    }
  ```
- Templated Components: Use `RenderFragment` and `RenderFragment<T>` for creating flexible, 
reusable components with customizable layouts.
- EventCallbacks: Use `EventCallback` for component outputs/events. Prefer `EventCallback<T>` for 
typed events.
  ```csharp
  // Good: Typed EventCallback
  // File: ChildComponent.razor
    <button @onclick="NotifyParent">Click Me</button>
  
  // File ChildComponent.razor.cs
    public partial class ChildComponent : ComponentBase
    {
        [Parameter]
        public EventCallback<string> OnClick { get; set; }
    
        private async Task NotifyParent()
        {
            await OnClick.InvokeAsync("Button Clicked!");
        }
    }
  ```
- Cascading Parameters: Use for sharing data down the component hierarchy (e.g., themes, user info), 
but sparingly to avoid over-coupling.

### Component Lifecycle
- Use appropriate lifecycle methods (`OnInitializedAsync`, 
`OnParametersSetAsync`, `ShouldRender`, `OnAfterRenderAsync`).
  - Perform async operations in `OnInitializedAsync` or `OnParametersSetAsync`.
  - Minimize work in `OnAfterRenderAsync`, primarily for JS interop after DOM is ready.

### Render Modes
- [DOTNET_VERSION]: Use the appropriate render mode for components
  - `@rendermode InteractiveServer`: Renders the component interactively on the server 
  using Blazor Server.
  - `@rendermode InteractiveWebAssembly`: Renders the component interactively on the client 
  using Blazor WebAssembly.
  - `@rendermode InteractiveAuto`: Initially uses Blazor Server, then switches to Blazor 
  WebAssembly on subsequent visits after the WASM bundle is downloaded.
  - `@rendermode StaticServer`: Renders the component statically as part of the page from the 
  server. No interactivity.
  - Prefer configuration per-component, per-page.
    ```csharp
    // Example: Applying a render mode to a component instance
      <MyInteractiveComponent @rendermode="InteractiveServer" />

    // Example: Applying a render mode to a component definition
    // MyComponent.razor
      @rendermode pageRenderMode
    // ... component content ...

    // Example: Set render mode in code-behind
    public partial class MyComponent : ComponentBase
    {
        private static IComponentRenderMode pageRenderMode = InteractiveServer
    }
    ```

### State Management
- Component State: For simple, localized state, use component parameters and fields.
- App-Level State:
  - Cascading Parameters: Suitable for simple global state.
  - Scoped Services: Register services (e.g., `AddScoped`) to manage state within a user's session 
  (Blazor Server) or for the lifetime of the app (Blazor WASM).
    ```csharp
    // Good: Scoped service for managing shopping cart state
    // Program.cs (or relevant startup)
      builder.Services.AddScoped<ShoppingCartState>();

    // ShoppingCartState.cs
      public class ShoppingCartState
      {
          public List<CartItem> Items { get; private set; } = new();
          public event Action OnChange;
          
          public void AddItem(CartItem item) 
          { 
              /* ... */ 
              NotifyStateChanged(); 
          }
          
          private void NotifyStateChanged() => OnChange?.Invoke();
      }
    ```
  - Flux/Redux Patterns: For complex applications, consider libraries like Fluxor or implement a similar pattern for predictable state changes.
- URL/Query Parameters: For state that should be bookmarkable or shareable.
- Local Storage/Session Storage: For persisting state on the client-side (primarily Blazor WASM or Blazor Server with JS interop).
- Persistent Component State: Use `PersistentComponentState` service to preserve component state across prerendering and full rendering, avoiding loss of state during initial load for interactive components.
  ```csharp
  // Good: Using PersistentComponentState
  // MyComponent.razor.cs
    public partial class MyComponent : ComponentBase, IDisposable 
    {
        [Inject]
        private PersistentComponentState ApplicationState

        private PersistingComponentStateSubscription persistingSubscription;
        private WeatherForecast[]? forecasts;
    
        protected override async Task OnInitializedAsync()
        {
            persistingSubscription = ApplicationState.RegisterOnPersisting(PersistForecasts);
      
            if (!ApplicationState.TryTakeFromJson<WeatherForecast[]>("weatherforecasts", out forecasts))
            {
                forecasts = await Http.GetFromJsonAsync<WeatherForecast>("WeatherForecast"); // Fetch if not persisted
            }
        }
    
        private Task PersistForecasts()
        {
            ApplicationState.PersistAsJson("weatherforecasts", forecasts);
            return Task.CompletedTask;
        }
    
        void IDisposable.Dispose() => persistingSubscription.Dispose();
    }
  ```

### Performance Optimizations
- `ShouldRender`: Implement `ShouldRender` to prevent unnecessary re-renders of components or subtrees.
  ```csharp
  // Good: Preventing re-render if parameters haven't changed
    public partial class MyComponent : ComponentBase
    {
        private int _previousValue;
        [Parameter] 
        public int Value { get; set; }
    
        protected override bool ShouldRender()
        {
            if (Value == _previousValue) return false;
            _previousValue = Value;
            return true;
        }
    }
    ```
- `@key` Directive: Use `@key` when rendering lists of components to help Blazor efficiently track and update elements.
    ```csharp
    // Good: Using @key for dynamic lists
      @foreach (var item in Items)
      {
          <MyItemComponent @key="item.Id" Item="item" />
      }
    ```
- Virtualization: Use the `<Virtualize>` component for rendering large lists efficiently.
- Async Operations: Use `async/await` correctly. Avoid `async void`.
- JS Interop: Minimize JS interop calls. Batch calls where possible.
- Code Splitting/Lazy Loading (Blazor WASM): Lazy load assemblies for routes or features not immediately needed.
    ```csharp
    // Good: Lazy loading an assembly for a specific route
    // App.razor
      <Router AppAssembly="@typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(MyLazyLoadedComponent).Assembly }">
    // ...
      </Router>
    ```
- AOT Compilation (Blazor WASM): Ahead-of-Time compilation can improve runtime performance at the cost of larger download sizes. Evaluate for [DOTNET_VERSION].
- Server-Side Rendering (SSR) with Streaming Rendering:
  - Leverage enhanced SSR for faster initial page loads.
  - Use streaming rendering (`@attribute [StreamRendering(true)]` on a routable component or `Html.RenderComponentAsync` with `renderMode: ServerStaticStreaming`) to progressively render UI parts as data becomes available, improving perceived performance.
    ```csharp
    // MyPage.razor
      @page "/streaming-data"
      @attribute [StreamRendering(true)]
      @inject MyDataService DataService
    
      <h1>Streaming Data Example</h1>
      <p>Content before await.</p>
      @await Task.Delay(1000) <!-- Simulate async work -->
      <p>Content after first await.</p>
    
      @foreach (var item in await DataService.GetItemsSlowlyAsync())
      {
        <p>@item.Name</p>
        @await Task.Delay(500) <!-- Simulate more async work per item -->
      }
      <p>All items loaded.</p>
    ```
- Sections API: Use `@rendermode` with sections (`<SectionOutlet>` and `<SectionContent>`) to define content placeholders that can be filled by other components, allowing for more flexible layouts with different render modes.

### Forms and Validation:
- Use `EditForm` component for handling forms.
- Implement validation using Data Annotations (`[Required]`, `[StringLength]`, etc.) on your model.
- Use built-in validation components like `DataAnnotationsValidator` and `ValidationSummary` or `ValidationMessage`.
- Custom Validation: Create custom `ValidationAttribute` or implement custom validation logic.
- Consider new form handling features or improvements in [DOTNET_VERSION] when available.

### Routing:
- Use `@page` directive for defining routes.
- Route Parameters: Define route parameters (e.g., `@page "/product/{Id:int}"`).
- `NavLink` Component: Use for navigation links to get active class styling.
- Programmatic Navigation: Use `NavigationManager` service.
- Route Constraints: Utilize route constraints for type checking and matching.

### JavaScript Interop:
- Use `IJSRuntime` to call JavaScript functions from C# and C# methods from JavaScript.
- Isolate JS interop calls in dedicated services or helper classes.
- Prefer unmarshalled JS interop for performance-critical scenarios in Blazor WebAssembly.
- Ensure JS code is placed in `wwwroot` and referenced correctly.
- Use JS initializers for setting up JS libraries or performing setup tasks when the Blazor app starts.

### Security:
- Authentication & Authorization: Use ASP.NET Core Identity or project specific provider (e.g., Azure AD B2C, OpenID Connect, Auth0, etc).
- Use `[Authorize]` attribute on components or `@attribute [Authorize]` in `.razor` files.
- `AuthorizeView` Component: Conditionally render UI based on authorization status.
- Protect against XSS: Blazor automatically encodes HTML, but be cautious with `MarkupString` and JS interop.
- Protect against CSRF: Blazor Server apps are generally protected by default. For Blazor WASM with custom backend APIs, ensure CSRF protection is implemented on the API. Antiforgery support is enhanced in .NET 8+ for form submissions.
- Content Security Policy (CSP): Implement a strong CSP.
- Secrets Management: Use user secrets in development and Azure Key Vault or similar in production.

### Error Handling:
- Error Boundaries: Use the `<ErrorBoundary>` component to catch exceptions within a part of the UI and display a fallback UI.
- Global Exception Handling: Implement custom error handling for unhandled exceptions.
- Logging: Use structured logging (e.g., Serilog, NLog) integrated with ASP.NET Core logging.

### Testing:
- bUnit: The recommended library for unit testing Blazor components.
  ```csharp
  // Good: bUnit test example
    using Bunit;
    using Xunit;
  
    public class CounterTests : TestContext
    {
      [Fact]
      public void CounterShouldIncrementWhenClicked()
      {
        // Arrange
        var cut = RenderComponent<Counter>();
  
        // Act
        cut.Find("button").Click();
  
        // Assert
        cut.Find("p").MarkupMatches("<p>Current count: 1</p>");
      }
    }
  ```
- Test Services: Mock or provide test implementations for services injected into components.
- E2E Integration Testing: Utilize Playwright for .NET using `Microsoft.Playwright.Xunit` package
- bUnit projects must be seperate from other test projects. Utilize the bUnit project template.
  - Use `dotnet new --install bunit.template` to install the template if it's missing
  - Use `dotnet new bunit --framework xunit -o <NAME OF TEST PROJECT>` to create the project

## Best Practices Summary
- Keep components small and focused.
- Use seperate code-behind, and isolated css files.
- Choose the appropriate **render mode** for each component or page.
- Prefer strong typing with `EventCallback<T>`.
- Manage state appropriately, using **Persistent Component State** for prerendered interactive components.
- Optimize rendering with `ShouldRender`, `@key`, and **Streaming Rendering**.
- Use `<Virtualize>` for large lists.
- Minimize and batch JS interop calls, using unmarshalled interop where beneficial.
- Implement robust authentication, authorization, and error handling.
- Write comprehensive tests using bUnit.
- Stay informed about and leverage new .NET 9 Blazor features.
- Follow official Microsoft Blazor documentation and community best practices.

## Troubleshooting:
- Browser Developer Tools: Inspect console errors, network requests, and component DOM.
- Debugging: Use Visual Studio debugger for Blazor Server, Blazor WebAssembly, and Blazor Hybrid.
- Logging: Check application logs for server-side errors or detailed client-side information.
- `.NET Aspire Dashboard` (preferred): Can provide insights into Blazor app behavior within a distributed system.
- Understand the implications of different render modes on debugging and state.
---
