#!/bin/perl
use strict;
my %states, my $url, my $pattern = $ARGV[0];
open(DATA, "<bookmarksbase.xml") or die "Error while opening file";
while(<DATA>)
{
	if ($_ =~/<Url>/)
	{
		($url) = $_ =~ /<Url>(.*)<\/Url>/;
		if ($_ =~ /$pattern/i && $states{$url} != 1)
		{
			print $url . "\n\n";
			$states{$url} = 1;
		}
	}
	else
	{	
		if ($_ =~ /$pattern/i && $states{$url} != 1)
		{
			print $url . "\n" . $_ . "\n";
			$states{$url} = 1;
		}
	}
}
close DATA;
