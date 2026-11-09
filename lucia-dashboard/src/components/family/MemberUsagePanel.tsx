import { useQuery } from '@tanstack/react-query'
import { getUsage } from '../../family-api'
import type { FamilyMember, UsageTotals } from '../../family-api'
import { secondaryButton, section, sectionHint, sectionTitle } from './familyStyles'

const usd = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 4 })
const count = new Intl.NumberFormat()

/** A member's model usage today and this month, against their limits, with the latest calls. */
export default function MemberUsagePanel({ member }: { member: FamilyMember }) {
  const usageQuery = useQuery({
    queryKey: ['family-usage', member.id],
    queryFn: ({ signal }) => getUsage(member.id, signal),
  })

  if (usageQuery.isLoading) {
    return <div role="status" aria-label="Loading usage" className="h-48 animate-pulse rounded-xl bg-basalt/70" />
  }
  if (usageQuery.isError) {
    return (
      <div role="alert" className="rounded-xl border border-rose/30 bg-rose/8 p-4 text-sm text-rose">
        <p>{usageQuery.error.message}</p>
        <button type="button" className={`mt-3 ${secondaryButton}`} onClick={() => void usageQuery.refetch()}>Try again</button>
      </div>
    )
  }

  const usage = usageQuery.data!
  return (
    <div className="space-y-6">
      <div className={section}>
        <h3 className={sectionTitle}>Spending</h3>
        <p className={sectionHint}>Estimated from token counts and each provider's prices. Local models without prices count as free.</p>
        <dl className="grid gap-4 sm:grid-cols-2">
          <Period label="Today" totals={usage.today} limit={member.llmControls.dailyBudgetUsd} />
          <Period label="This month" totals={usage.month} limit={member.llmControls.monthlyBudgetUsd} />
        </dl>
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Recent model calls</h3>
        {usage.recent.length === 0 ? (
          <p className="rounded-xl border border-stone p-4 text-sm text-fog">
            No model calls yet. They appear here once {member.displayName} talks to Lucia from a paired device or a linked voice.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-xl border border-stone">
            <table className="w-full min-w-[34rem] text-left text-sm">
              <thead className="border-b border-stone text-xs text-dust">
                <tr>
                  <th scope="col" className="px-4 py-2 font-medium">Time</th>
                  <th scope="col" className="px-4 py-2 font-medium">Provider</th>
                  <th scope="col" className="px-4 py-2 text-right font-medium">Tokens in / out</th>
                  <th scope="col" className="px-4 py-2 text-right font-medium">Cost</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone">
                {usage.recent.map(record => (
                  <tr key={record.id}>
                    <td className="whitespace-nowrap px-4 py-2 text-fog">{new Date(record.timestamp).toLocaleString()}</td>
                    <td className="px-4 py-2 text-cloud">
                      {record.providerId}
                      {record.model && <span className="block text-xs text-dust">{record.model}</span>}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-cloud">
                      {count.format(record.inputTokens)} / {count.format(record.outputTokens)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-light">{usd.format(record.costUsd)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}

function Period({ label, totals, limit }: { label: string; totals: UsageTotals; limit?: number | null }) {
  const ratio = limit ? Math.min(1, totals.costUsd / limit) : 0
  const reached = limit != null && totals.costUsd >= limit
  return (
    <div className="rounded-xl border border-stone p-4">
      <dt className="text-xs font-medium text-fog">{label}</dt>
      <dd className="mt-1">
        <span className="font-display text-xl font-semibold tabular-nums text-light">{usd.format(totals.costUsd)}</span>
        <span className="ml-2 text-sm text-dust">{limit != null ? `of ${usd.format(limit)}` : 'no limit'}</span>
      </dd>
      {limit != null && (
        <dd className="mt-3">
          <div role="meter" aria-label={`${label} spending`} aria-valuemin={0} aria-valuemax={limit} aria-valuenow={totals.costUsd}
            className="h-1.5 overflow-hidden rounded-full bg-stone">
            <div className={`h-full rounded-full ${reached ? 'bg-rose' : 'bg-amber'}`} style={{ width: `${ratio * 100}%` }} />
          </div>
          {reached && <p className="mt-2 text-xs text-rose">Limit reached. New requests are refused until the period resets.</p>}
        </dd>
      )}
      <dd className="mt-3 text-xs text-dust">
        {count.format(totals.calls)} calls · {count.format(totals.inputTokens + totals.outputTokens)} tokens
      </dd>
    </div>
  )
}
