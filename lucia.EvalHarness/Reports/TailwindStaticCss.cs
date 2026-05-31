namespace lucia.EvalHarness.Reports;

/// <summary>
/// Offline utility CSS that replaces the Tailwind CDN for generated HTML reports.
/// Covers every utility class used in report-template.html so the report renders
/// correctly without network access.
/// </summary>
internal static class TailwindStaticCss
{
    internal const string Css = """
        /* ── Offline Tailwind-compatible utility CSS for lucia EvalHarness ── */
        /* Covers all utility classes used in report-template.html             */

        /* ── Preflight / Reset ──────────────────────────────────────────── */
        *, ::before, ::after { box-sizing: border-box; border-width: 0; border-style: solid; border-color: currentColor; }
        html { line-height: 1.5; -webkit-text-size-adjust: 100%; tab-size: 4;
               font-family: ui-sans-serif, system-ui, -apple-system, sans-serif; }
        body { margin: 0; line-height: inherit; }
        hr { height: 0; color: inherit; border-top-width: 1px; }
        h1, h2, h3, h4, h5, h6 { font-size: inherit; font-weight: inherit; margin: 0; }
        a { color: inherit; text-decoration: inherit; }
        b, strong { font-weight: bolder; }
        code, kbd, samp, pre { font-family: ui-monospace, monospace; font-size: 1em; }
        pre { margin: 0; }
        small { font-size: 80%; }
        table { text-indent: 0; border-color: inherit; border-collapse: collapse; }
        button, input, optgroup, select, textarea {
          font-family: inherit; font-size: 100%; font-weight: inherit;
          line-height: inherit; color: inherit; margin: 0; padding: 0; }
        button, select { text-transform: none; cursor: pointer; }
        button, [type='button'], [type='reset'], [type='submit'] {
          -webkit-appearance: button; background-color: transparent; background-image: none; }
        img, svg, video, canvas, audio, iframe, embed, object { display: block; vertical-align: middle; }
        img, video { max-width: 100%; height: auto; }
        ol, ul, menu { list-style: none; margin: 0; padding: 0; }
        blockquote, dl, dd, p, figure { margin: 0; }
        [hidden] { display: none; }

        /* ── Display ─────────────────────────────────────────────────────── */
        .hidden       { display: none; }
        .block        { display: block; }
        .inline-flex  { display: inline-flex; }
        .inline-grid  { display: inline-grid; }
        .flex         { display: flex; }
        .grid         { display: grid; }

        /* ── Position ────────────────────────────────────────────────────── */
        .sticky   { position: sticky; }
        .fixed    { position: fixed; }
        .relative { position: relative; }
        .inset-0  { inset: 0px; }
        .top-0    { top: 0px; }
        .z-1      { z-index: 1; }
        .z-50     { z-index: 50; }

        /* ── Flexbox ─────────────────────────────────────────────────────── */
        .flex-1          { flex: 1 1 0%; }
        .flex-wrap       { flex-wrap: wrap; }
        .shrink-0        { flex-shrink: 0; }
        .items-center    { align-items: center; }
        .items-start     { align-items: flex-start; }
        .items-end       { align-items: flex-end; }
        .self-center     { align-self: center; }
        .justify-between { justify-content: space-between; }
        .justify-center  { justify-content: center; }
        .justify-end     { justify-content: flex-end; }

        /* ── Grid ────────────────────────────────────────────────────────── */
        .grid-cols-1 { grid-template-columns: repeat(1, minmax(0, 1fr)); }
        .grid-cols-2 { grid-template-columns: repeat(2, minmax(0, 1fr)); }
        .grid-cols-3 { grid-template-columns: repeat(3, minmax(0, 1fr)); }
        .col-span-2  { grid-column: span 2 / span 2; }

        /* ── Gap ─────────────────────────────────────────────────────────── */
        .gap-1   { gap: 0.25rem; }
        .gap-2   { gap: 0.5rem; }
        .gap-3   { gap: 0.75rem; }
        .gap-4   { gap: 1rem; }
        .gap-5   { gap: 1.25rem; }
        .gap-6   { gap: 1.5rem; }
        .gap-x-5 { column-gap: 1.25rem; }
        .gap-y-1 { row-gap: 0.25rem; }

        /* ── Space Y ─────────────────────────────────────────────────────── */
        .space-y-2   > :not([hidden]) ~ :not([hidden]) { margin-top: 0.5rem; }
        .space-y-2\.5 > :not([hidden]) ~ :not([hidden]) { margin-top: 0.625rem; }
        .space-y-3   > :not([hidden]) ~ :not([hidden]) { margin-top: 0.75rem; }
        .space-y-4   > :not([hidden]) ~ :not([hidden]) { margin-top: 1rem; }
        .space-y-6   > :not([hidden]) ~ :not([hidden]) { margin-top: 1.5rem; }

        /* ── Overflow ────────────────────────────────────────────────────── */
        .overflow-hidden  { overflow: hidden; }
        .overflow-x-auto  { overflow-x: auto; }

        /* ── Sizing ──────────────────────────────────────────────────────── */
        .w-2         { width: 0.5rem; }
        .w-4         { width: 1rem; }
        .w-7         { width: 1.75rem; }
        .w-full      { width: 100%; }
        .h-2         { height: 0.5rem; }
        .h-2\.5      { height: 0.625rem; }
        .h-4         { height: 1rem; }
        .h-7         { height: 1.75rem; }
        .min-h-screen { min-height: 100vh; }
        .max-w-xs    { max-width: 20rem; }
        .max-w-2xl   { max-width: 42rem; }
        .max-w-\[1400px\] { max-width: 1400px; }
        .max-w-\[85\%\]   { max-width: 85%; }

        /* ── Margin ──────────────────────────────────────────────────────── */
        .mx-auto  { margin-left: auto; margin-right: auto; }
        .ml-4     { margin-left: 1rem; }
        .ml-auto  { margin-left: auto; }
        .mt-0\.5  { margin-top: 0.125rem; }
        .mt-1     { margin-top: 0.25rem; }
        .mt-3     { margin-top: 0.75rem; }
        .mt-4     { margin-top: 1rem; }
        .mb-1     { margin-bottom: 0.25rem; }
        .mb-2     { margin-bottom: 0.5rem; }
        .mb-3     { margin-bottom: 0.75rem; }
        .mb-4     { margin-bottom: 1rem; }
        .mb-6     { margin-bottom: 1.5rem; }
        .pt-4     { padding-top: 1rem; }
        .pb-5     { padding-bottom: 1.25rem; }
        .pr-3     { padding-right: 0.75rem; }

        /* ── Padding ─────────────────────────────────────────────────────── */
        .p-2      { padding: 0.5rem; }
        .px-2     { padding-left: 0.5rem;  padding-right: 0.5rem; }
        .px-3     { padding-left: 0.75rem; padding-right: 0.75rem; }
        .px-4     { padding-left: 1rem;    padding-right: 1rem; }
        .px-5     { padding-left: 1.25rem; padding-right: 1.25rem; }
        .px-6     { padding-left: 1.5rem;  padding-right: 1.5rem; }
        .py-0\.5  { padding-top: 0.125rem; padding-bottom: 0.125rem; }
        .py-1     { padding-top: 0.25rem;  padding-bottom: 0.25rem; }
        .py-1\.5  { padding-top: 0.375rem; padding-bottom: 0.375rem; }
        .py-2     { padding-top: 0.5rem;   padding-bottom: 0.5rem; }
        .py-2\.5  { padding-top: 0.625rem; padding-bottom: 0.625rem; }
        .py-3     { padding-top: 0.75rem;  padding-bottom: 0.75rem; }
        .py-6     { padding-top: 1.5rem;   padding-bottom: 1.5rem; }
        .py-8     { padding-top: 2rem;     padding-bottom: 2rem; }
        .py-12    { padding-top: 3rem;     padding-bottom: 3rem; }

        /* ── Typography ──────────────────────────────────────────────────── */
        .text-xs    { font-size: 0.75rem;  line-height: 1rem; }
        .text-sm    { font-size: 0.875rem; line-height: 1.25rem; }
        .text-lg    { font-size: 1.125rem; line-height: 1.75rem; }
        .text-2xl   { font-size: 1.5rem;   line-height: 2rem; }
        .text-3xl   { font-size: 1.875rem; line-height: 2.25rem; }
        .text-\[11px\] { font-size: 11px; }
        .text-\[10px\] { font-size: 10px; }

        .font-normal   { font-weight: 400; }
        .font-medium   { font-weight: 500; }
        .font-semibold { font-weight: 600; }
        .font-bold     { font-weight: 700; }
        .font-black    { font-weight: 900; }
        .font-mono { font-family: ui-monospace, "Cascadia Code", "JetBrains Mono", "Fira Code", monospace; }
        .font-sans { font-family: "SF Pro Display", "Segoe UI", system-ui, -apple-system, sans-serif; }

        .uppercase       { text-transform: uppercase; }
        .italic          { font-style: italic; }
        .not-italic      { font-style: normal; }
        .tracking-tight  { letter-spacing: -0.025em; }
        .tracking-wider  { letter-spacing: 0.05em; }
        .tracking-widest { letter-spacing: 0.1em; }
        .leading-none    { line-height: 1; }
        .truncate        { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .whitespace-nowrap { white-space: nowrap; }
        .text-center { text-align: center; }
        .text-left   { text-align: left; }
        .text-right  { text-align: right; }
        .antialiased { -webkit-font-smoothing: antialiased; -moz-osx-font-smoothing: grayscale; }

        /* ── Borders ─────────────────────────────────────────────────────── */
        .rounded      { border-radius: 0.25rem; }
        .rounded-full { border-radius: 9999px; }
        .rounded-lg   { border-radius: 0.5rem; }
        .border       { border-width: 1px; }

        /* ── Custom color classes (CSS-var-backed) ────────────────────────── */
        .text-accent { color: var(--c-accent); }

        /* ── Effects / Interaction ───────────────────────────────────────── */
        .backdrop-blur-xl { backdrop-filter: blur(24px); -webkit-backdrop-filter: blur(24px); }
        .pointer-events-none { pointer-events: none; }
        .cursor-pointer { cursor: pointer; }
        .cursor-default { cursor: default; }
        .select-none    { user-select: none; -webkit-user-select: none; }
        .transition-colors {
          transition-property: color, background-color, border-color, text-decoration-color, fill, stroke;
          transition-timing-function: cubic-bezier(0.4, 0, 0.2, 1);
          transition-duration: 150ms; }
        .hover\:bg-\[var\(--c-surface2\)\]:hover { background-color: var(--c-surface2); }

        /* ── Responsive ──────────────────────────────────────────────────── */
        @media (min-width: 640px) {
          .sm\:px-6       { padding-left: 1.5rem; padding-right: 1.5rem; }
          .sm\:grid-cols-2 { grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .sm\:grid-cols-3 { grid-template-columns: repeat(3, minmax(0, 1fr)); }
          .sm\:grid-cols-4 { grid-template-columns: repeat(4, minmax(0, 1fr)); }
        }
        @media (min-width: 768px) {
          .md\:grid-cols-6 { grid-template-columns: repeat(6, minmax(0, 1fr)); }
        }
        @media (min-width: 1024px) {
          .lg\:col-span-2  { grid-column: span 2 / span 2; }
          .lg\:grid-cols-3 { grid-template-columns: repeat(3, minmax(0, 1fr)); }
        }

        /* ── Dark mode (class strategy) ──────────────────────────────────── */
        .dark .dark\:hidden  { display: none; }
        .dark .dark\:inline  { display: inline; }
        """;
}
