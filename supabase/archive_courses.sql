-- Silinen dersler artık arşivlenir: listelerden kalkar, çalışma kayıtları istatistik ve geçmişte kalır.
-- Supabase > SQL Editor'da bir kez çalıştırın. Çalıştırılmazsa senkron yine çalışır,
-- yalnızca bir dersin silindiği bilgisi diğer cihaza geçmez.
alter table public.courses add column if not exists archived boolean not null default false;

-- PostgREST'in yeni sütunu hemen görmesi için:
notify pgrst, 'reload schema';
