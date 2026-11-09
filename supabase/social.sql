-- Flow State: arkadaşlar, canlı durum ve odalar (lider tablosu).
-- Supabase panelinde SQL Editor'e yapıştırıp bir kez çalıştırın (live_session.sql'den SONRA).
-- Tekrar çalıştırmak güvenlidir.
--
-- Güvenlik: yeni tablolara doğrudan erişim yok (RLS açık, politika yok). Her şey aşağıdaki
-- "security definer" fonksiyonlardan geçer; her fonksiyon çağıranın kim olduğunu (auth.uid())
-- ve arkadaş/üye olup olmadığını kendisi denetler.

-- ---------- tablolar ----------

alter table public.live_session add column if not exists seen_at timestamptz not null default now();

create table if not exists public.profiles (
  user_id      uuid primary key references auth.users(id) on delete cascade,
  username     text not null unique check (username ~ '^[a-z0-9_]{3,20}$'),
  share_status boolean not null default true,   -- canlı durumu arkadaşlara göster
  created_at   timestamptz not null default now()
);

create table if not exists public.friendships (
  user_a     uuid not null references auth.users(id) on delete cascade,
  user_b     uuid not null references auth.users(id) on delete cascade,
  requester  uuid not null,
  status     text not null default 'pending' check (status in ('pending', 'accepted')),
  created_at timestamptz not null default now(),
  primary key (user_a, user_b),
  check (user_a < user_b)
);
create index if not exists friendships_b_idx on public.friendships (user_b);

create table if not exists public.rooms (
  id          uuid primary key default gen_random_uuid(),
  name        text not null check (char_length(name) between 1 and 40),
  owner       uuid not null references auth.users(id) on delete cascade,
  code        text not null unique,
  period_days int  not null default 7 check (period_days between 1 and 366),
  created_at  timestamptz not null default now()
);

create table if not exists public.room_members (
  room_id    uuid not null references public.rooms(id) on delete cascade,
  user_id    uuid not null references auth.users(id) on delete cascade,
  status     text not null default 'member' check (status in ('invited', 'member')),
  invited_by uuid,
  joined_at  timestamptz not null default now(),
  primary key (room_id, user_id)
);
create index if not exists room_members_user_idx on public.room_members (user_id);

alter table public.profiles     enable row level security;
alter table public.friendships  enable row level security;
alter table public.rooms        enable row level security;
alter table public.room_members enable row level security;
revoke all on public.profiles, public.friendships, public.rooms, public.room_members from anon, authenticated;

-- ---------- iç yardımcılar ----------

create or replace function public.fs_uid() returns uuid
language plpgsql stable as $$
declare u uuid := auth.uid();
begin
  if u is null then raise exception 'not_authenticated'; end if;
  return u;
end $$;

create or replace function public.fs_need_profile(u uuid) returns void
language plpgsql stable as $$
begin
  if not exists (select 1 from public.profiles where user_id = u) then
    raise exception 'no_profile';
  end if;
end $$;

-- Bir kullanıcının canlı durumu: working | paused | offline. Süre sunucu saatiyle hesaplanır.
-- 5 dakikadır sinyal (seen_at) vermeyen cihaz çevrimdışı sayılır (uygulama kapandı / çöktü).
create or replace function public.fs_live(p_user uuid) returns jsonb
language sql stable as $$
  select case
    when ls.user_id is null or not ls.active or ls.seen_at < now() - interval '5 minutes'
      then jsonb_build_object('state', 'offline', 'elapsed', 0)
    when ls.running_since is not null
      then jsonb_build_object('state', 'working',
             'elapsed', ls.accumulated + extract(epoch from (now() - ls.running_since)))
    else jsonb_build_object('state', 'paused', 'elapsed', ls.accumulated)
  end
  from (select p_user as uid) q
  left join public.live_session ls on ls.user_id = q.uid
$$;

create or replace function public.fs_secs(p_user uuid, p_since timestamptz) returns bigint
language sql stable as $$
  select coalesce(sum(s.seconds), 0)::bigint
  from public.sessions s
  where s.user_id = p_user and s.started_at >= p_since and not s.deleted
$$;

-- ---------- profil ----------

create or replace function public.social_me() returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare u uuid := fs_uid(); r jsonb;
begin
  select jsonb_build_object('username', username, 'share_status', share_status)
    into r from profiles where user_id = u;
  return r;
end $$;

create or replace function public.social_set_profile(p_username text, p_share boolean) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid(); n text := lower(trim(p_username));
begin
  if n !~ '^[a-z0-9_]{3,20}$' then raise exception 'invalid_username'; end if;
  insert into profiles (user_id, username, share_status) values (u, n, coalesce(p_share, true))
  on conflict (user_id) do update set username = excluded.username, share_status = excluded.share_status;
exception when unique_violation then
  raise exception 'username_taken';
end $$;

-- Cihaz açık ve seans sürüyor sinyali (istemci ~45 sn'de bir çağırır).
create or replace function public.social_heartbeat() returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  update live_session set seen_at = now() where user_id = u;
end $$;

-- ---------- arkadaşlar ----------

create or replace function public.social_friends(p_since timestamptz) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  return coalesce((
    select jsonb_agg(x.j order by x.ord, x.uname)
    from (
      select
        case when f.status = 'accepted' then 0 when f.requester <> u then 1 else 2 end as ord,
        p.username as uname,
        jsonb_build_object(
          'user_id', p.user_id,
          'username', p.username,
          'rel', case when f.status = 'accepted' then 'friend'
                      when f.requester <> u then 'incoming' else 'outgoing' end,
          'hidden', f.status = 'accepted' and not p.share_status,
          'live', case when f.status = 'accepted' and p.share_status then fs_live(p.user_id) else null end,
          'today', case when f.status = 'accepted' and p.share_status then fs_secs(p.user_id, p_since) else null end
        ) as j
      from friendships f
      join profiles p on p.user_id = case when f.user_a = u then f.user_b else f.user_a end
      where f.user_a = u or f.user_b = u
    ) x
  ), '[]'::jsonb);
end $$;

create or replace function public.social_friend_request(p_username text) returns text
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid(); t uuid; f friendships%rowtype; a uuid; b uuid;
begin
  perform fs_need_profile(u);
  select user_id into t from profiles where username = lower(trim(p_username));
  if t is null then raise exception 'user_not_found'; end if;
  if t = u then raise exception 'self'; end if;
  a := least(u, t); b := greatest(u, t);
  select * into f from friendships where user_a = a and user_b = b;
  if found then
    if f.status = 'accepted' then raise exception 'already_friends'; end if;
    if f.requester = u then raise exception 'already_requested'; end if;
    update friendships set status = 'accepted' where user_a = a and user_b = b;  -- karşı taraf zaten istemişti
    return 'accepted';
  end if;
  if (select count(*) from friendships where user_a = u or user_b = u) >= 200 then
    raise exception 'too_many_friends';
  end if;
  insert into friendships (user_a, user_b, requester) values (a, b, u);
  return 'sent';
end $$;

-- Gelen isteği kabul et / reddet.
create or replace function public.social_friend_respond(p_user uuid, p_accept boolean) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid(); a uuid := least(u, p_user); b uuid := greatest(u, p_user);
begin
  if p_accept then
    update friendships set status = 'accepted'
      where user_a = a and user_b = b and status = 'pending' and requester = p_user;
  else
    delete from friendships where user_a = a and user_b = b and status = 'pending' and requester = p_user;
  end if;
end $$;

-- Arkadaşlıktan çık ya da gönderdiğin isteği geri çek.
create or replace function public.social_friend_remove(p_user uuid) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  delete from friendships where user_a = least(u, p_user) and user_b = greatest(u, p_user);
end $$;

-- ---------- odalar ----------

create or replace function public.social_rooms() returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  return coalesce((
    select jsonb_agg(jsonb_build_object(
      'id', r.id, 'name', r.name,
      'code', case when m.status = 'member' then r.code else null end,
      'period_days', r.period_days,
      'is_owner', r.owner = u,
      'status', m.status,
      'members', (select count(*) from room_members x where x.room_id = r.id and x.status = 'member'),
      'invited_by', (select p.username from profiles p where p.user_id = m.invited_by)
    ) order by (m.status = 'invited') desc, r.created_at)
    from room_members m join rooms r on r.id = m.room_id
    where m.user_id = u
  ), '[]'::jsonb);
end $$;

create or replace function public.social_room_create(p_name text, p_days int) returns jsonb
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid(); n text := trim(p_name); c text; rid uuid;
begin
  perform fs_need_profile(u);
  if char_length(n) < 1 or char_length(n) > 40 then raise exception 'invalid_name'; end if;
  if (select count(*) from rooms where owner = u) >= 10 then raise exception 'too_many_rooms'; end if;
  loop
    c := upper(substr(md5(random()::text || clock_timestamp()::text), 1, 6));
    exit when not exists (select 1 from rooms where code = c);
  end loop;
  insert into rooms (name, owner, code, period_days)
    values (n, u, c, greatest(1, least(coalesce(p_days, 7), 366))) returning id into rid;
  insert into room_members (room_id, user_id, status) values (rid, u, 'member');
  return jsonb_build_object('id', rid, 'code', c);
end $$;

-- Davet koduyla odaya katıl.
create or replace function public.social_room_join(p_code text) returns uuid
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid(); r rooms%rowtype;
begin
  perform fs_need_profile(u);
  select * into r from rooms where code = upper(trim(p_code));
  if not found then raise exception 'room_not_found'; end if;
  if (select count(*) from room_members where room_id = r.id and status = 'member') >= 30
     and not exists (select 1 from room_members where room_id = r.id and user_id = u and status = 'member') then
    raise exception 'room_full';
  end if;
  insert into room_members (room_id, user_id, status) values (r.id, u, 'member')
  on conflict (room_id, user_id) do update set status = 'member';
  return r.id;
end $$;

-- Odadaki bir üye, kendi arkadaşını davet edebilir.
create or replace function public.social_room_invite(p_room uuid, p_user uuid) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  if not exists (select 1 from room_members where room_id = p_room and user_id = u and status = 'member') then
    raise exception 'not_member';
  end if;
  if not exists (select 1 from friendships where user_a = least(u, p_user) and user_b = greatest(u, p_user)
                 and status = 'accepted') then
    raise exception 'not_friends';
  end if;
  if (select count(*) from room_members where room_id = p_room) >= 40 then raise exception 'room_full'; end if;
  insert into room_members (room_id, user_id, status, invited_by) values (p_room, p_user, 'invited', u)
  on conflict (room_id, user_id) do nothing;
end $$;

create or replace function public.social_room_respond(p_room uuid, p_accept boolean) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  if p_accept then
    perform fs_need_profile(u);
    update room_members set status = 'member', joined_at = now()
      where room_id = p_room and user_id = u and status = 'invited';
  else
    delete from room_members where room_id = p_room and user_id = u and status = 'invited';
  end if;
end $$;

-- Üye ayrılır; oda sahibi ayrılırsa oda silinir.
create or replace function public.social_room_leave(p_room uuid) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  if exists (select 1 from rooms where id = p_room and owner = u) then
    delete from rooms where id = p_room;
  else
    delete from room_members where room_id = p_room and user_id = u;
  end if;
end $$;

create or replace function public.social_room_kick(p_room uuid, p_user uuid) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  if not exists (select 1 from rooms where id = p_room and owner = u) then raise exception 'not_owner'; end if;
  if p_user = u then raise exception 'self'; end if;
  delete from room_members where room_id = p_room and user_id = p_user;
end $$;

create or replace function public.social_room_set_period(p_room uuid, p_days int) returns void
language plpgsql security definer set search_path = public as $$
declare u uuid := fs_uid();
begin
  update rooms set period_days = greatest(1, least(coalesce(p_days, 7), 366)) where id = p_room and owner = u;
  if not found then raise exception 'not_owner'; end if;
end $$;

-- Lider tablosu: p_since'ten (genelde yerel gün başlangıcı) bugüne her üyenin toplam saniyesi + canlı durumu.
create or replace function public.social_room_board(p_room uuid, p_since timestamptz) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare u uuid := fs_uid(); rm rooms%rowtype;
begin
  if not exists (select 1 from room_members where room_id = p_room and user_id = u and status = 'member') then
    raise exception 'not_member';
  end if;
  select * into rm from rooms where id = p_room;
  return jsonb_build_object(
    'id', rm.id, 'name', rm.name, 'code', rm.code, 'period_days', rm.period_days, 'is_owner', rm.owner = u,
    'members', coalesce((
      select jsonb_agg(jsonb_build_object(
        'user_id', m.user_id, 'username', p.username, 'status', m.status,
        'is_owner', m.user_id = rm.owner, 'is_me', m.user_id = u,
        'seconds', case when m.status = 'member' then fs_secs(m.user_id, p_since) else 0 end,
        'hidden', not p.share_status and m.user_id <> u,
        'live', case when m.status = 'member' and (p.share_status or m.user_id = u)
                     then fs_live(m.user_id) else null end
      ))
      from room_members m join profiles p on p.user_id = m.user_id
      where m.room_id = p_room
    ), '[]'::jsonb)
  );
end $$;

-- ---------- yetkiler ----------

revoke all on function public.fs_uid(), public.fs_need_profile(uuid), public.fs_live(uuid),
                       public.fs_secs(uuid, timestamptz) from public, anon, authenticated;

do $$
declare r record;
begin
  for r in
    select p.oid::regprocedure as sig
    from pg_proc p join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public' and p.proname like 'social\_%'
  loop
    execute format('revoke all on function %s from public, anon', r.sig);
    execute format('grant execute on function %s to authenticated', r.sig);
  end loop;
end $$;
