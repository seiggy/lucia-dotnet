/** Props that connect a tab panel to the tab rendered by {@link import('./Tabs').default}. */
export function tabPanelProps(idPrefix: string, id: string) {
  return {
    role: 'tabpanel' as const,
    id: `${idPrefix}-panel-${id}`,
    'aria-labelledby': `${idPrefix}-tab-${id}`,
    tabIndex: 0,
  }
}
