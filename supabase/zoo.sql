-- Flow State: hayvanat bahçesi — satın alınan hayvanlar.
-- Supabase panelinde SQL Editor'e yapıştırıp bir kez çalıştırın.
-- Bakiye saklanmaz; istemci "kazanılan − harcanan" olarak hesaplar.

create table if not exists public.owned_animals (
  user_id    uuid        not null default auth.uid() references auth.users(id) on delete cascade,
  animal_id  text        not null,
  price      integer     not null check (price >= 0),
  bought_at  timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  synced_at  timestamptz not null default now(),
  deleted    boolean     not null default false,
  primary key (user_id, animal_id)
);

-- synced_at: her yazımda sunucu saatiyle güncellenir (istemci bundan sonrasını çeker).
create or replace function public.zoo_touch_synced_at() returns trigger
language plpgsql as $$
begin
  new.synced_at := clock_timestamp();
  return new;
end $$;

drop trigger if exists owned_animals_synced_at on public.owned_animals;
create trigger owned_animals_synced_at before insert or update on public.owned_animals
  for each row execute function public.zoo_touch_synced_at();

create index if not exists owned_animals_user_synced on public.owned_animals (user_id, synced_at);

alter table public.owned_animals enable row level security;

drop policy if exists "owned_animals: kendi satirlarim" on public.owned_animals;
create policy "owned_animals: kendi satirlarim" on public.owned_animals
  for all to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());
