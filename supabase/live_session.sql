-- Flow State: cihazlar arası "şu an çalışılan seans" senkronu.
-- Supabase panelinde SQL Editor'e yapıştırıp bir kez çalıştırın.
-- Kullanıcı başına tek satır; en yeni updated_at kazanır.

create table if not exists public.live_session (
  user_id       uuid primary key default auth.uid() references auth.users(id) on delete cascade,
  active        boolean          not null default false,
  run_id        text             not null default '',
  course_id     text             not null default '',
  started_at    timestamptz,
  accumulated   double precision not null default 0,
  running_since timestamptz,
  updated_at    timestamptz      not null default now()
);

alter table public.live_session enable row level security;

drop policy if exists "live_session: kendi satirim" on public.live_session;
create policy "live_session: kendi satirim" on public.live_session
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());
